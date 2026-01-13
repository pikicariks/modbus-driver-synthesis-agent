using AiAgents.SolarDriverAgent.Application.Contracts;
using AiAgents.SolarDriverAgent.Domain.Entities;
using AiAgents.SolarDriverAgent.Domain.Enums;
using AiAgents.SolarDriverAgent.Domain.Repositories;
using AiAgents.SolarDriverAgent.Web.Hubs;
using AiAgents.SolarDriverAgent.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AiAgents.SolarDriverAgent.Web.Controllers;

/// <summary>
/// "Tanak" API kontroler - samo validira, upisuje u bazu i vraća status.
/// Sva logika odlučivanja je u Runneru.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly IProtocolTaskRepository _taskRepository;
    private readonly ISimulationLogRepository _logRepository;
    private readonly IPdfValidator _pdfValidator;
    private readonly IHubContext<AgentHub, IAgentHubClient> _hubContext;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        IProtocolTaskRepository taskRepository,
        ISimulationLogRepository logRepository,
        IPdfValidator pdfValidator,
        IHubContext<AgentHub, IAgentHubClient> hubContext,
        ILogger<TasksController> logger)
    {
        _taskRepository = taskRepository;
        _logRepository = logRepository;
        _pdfValidator = pdfValidator;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Upload PDF dokumentacije za generisanje drajvera.
    /// Validira PDF prije upisivanja u bazu.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UploadTaskResponse>> UploadPdf(
        [FromForm] string deviceName,
        IFormFile pdfFile,
        CancellationToken cancellationToken)
    {
        if (pdfFile == null || pdfFile.Length == 0)
        {
            return BadRequest(new { error = "PDF file is required" });
        }

        if (!pdfFile.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !pdfFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "File must be a PDF document" });
        }

        using var memoryStream = new MemoryStream();
        await pdfFile.CopyToAsync(memoryStream, cancellationToken);
        var pdfBytes = memoryStream.ToArray();

        var validationResult = _pdfValidator.Validate(pdfBytes);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "PDF validation failed for '{FileName}': {Error}",
                pdfFile.FileName, validationResult.ErrorMessage);

            return BadRequest(new
            {
                error = "PDF validation failed",
                details = validationResult.ErrorMessage,
                fileSize = validationResult.FileSize
            });
        }

        if (!validationResult.HasExtractableText)
        {
            _logger.LogWarning(
                "PDF '{FileName}' has no extractable text - may be scanned/image-based",
                pdfFile.FileName);
        }

        var task = ProtocolTask.Create(deviceName ?? "Unknown Device", pdfBytes);
        await _taskRepository.AddAsync(task, cancellationToken);

        _logger.LogInformation(
            "Created task {TaskId} for device '{DeviceName}' ({Pages} pages, {Size} bytes, hasText: {HasText})",
            task.Id, task.DeviceName,
            validationResult.PageCount,
            validationResult.FileSize,
            validationResult.HasExtractableText);

        await _hubContext.Clients.All.TaskCreated(new TaskCreatedNotification
        {
            TaskId = task.Id,
            DeviceName = task.DeviceName,
            CreatedAt = task.CreatedAt,
            PageCount = validationResult.PageCount ?? 0,
            FileSizeBytes = validationResult.FileSize
        });

        return CreatedAtAction(
            nameof(GetTaskStatus),
            new { taskId = task.Id },
            new UploadTaskResponse
            {
                TaskId = task.Id,
                DeviceName = task.DeviceName,
                Status = task.Status.ToString(),
                CreatedAt = task.CreatedAt,
                PdfInfo = new PdfInfo
                {
                    PageCount = validationResult.PageCount ?? 0,
                    FileSizeBytes = validationResult.FileSize,
                    PdfVersion = validationResult.PdfVersion,
                    HasExtractableText = validationResult.HasExtractableText
                }
            });
    }

    /// <summary>
    /// Dohvata status zadatka.
    /// </summary>
    [HttpGet("{taskId:guid}")]
    public async Task<ActionResult<TaskStatusResponse>> GetTaskStatus(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken);
        if (task == null)
        {
            return NotFound(new { error = $"Task {taskId} not found" });
        }

        string? lastError = null;
        var logs = await _logRepository.GetRecentFailuresAsync(taskId, 1, cancellationToken);
        if (logs.Count > 0)
        {
            lastError = logs[0].ErrorMessage;
        }

        return Ok(MapToResponse(task, lastError));
    }

    /// <summary>
    /// Dohvata generisani drajver kod.
    /// </summary>
    [HttpGet("{taskId:guid}/driver")]
    public async Task<ActionResult<string>> GetDriverCode(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(taskId, cancellationToken);
        if (task == null)
        {
            return NotFound(new { error = $"Task {taskId} not found" });
        }

        if (task.CurrentDriver == null)
        {
            return NotFound(new { error = "Driver not yet generated for this task" });
        }

        return Ok(new
        {
            taskId = task.Id,
            deviceName = task.DeviceName,
            driverCode = task.CurrentDriver.SourceCode,
            version = task.CurrentDriver.Version,
            isValidated = task.CurrentDriver.IsValidated,
            generatedAt = task.CurrentDriver.GeneratedAt
        });
    }

    /// <summary>
    /// Lista svih zadataka sa paginacijom.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TaskListResponse>> GetAllTasks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        ProtocolTaskStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<ProtocolTaskStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        var result = await _taskRepository.GetAllAsync(page, pageSize, statusFilter, cancellationToken);

        var taskResponses = new List<TaskStatusResponse>();
        foreach (var task in result.Items)
        {
            string? lastError = null;
            if (task.Status == ProtocolTaskStatus.Failed)
            {
                var logs = await _logRepository.GetRecentFailuresAsync(task.Id, 1, cancellationToken);
                lastError = logs.FirstOrDefault()?.ErrorMessage;
            }

            taskResponses.Add(MapToResponse(task, lastError));
        }

        return Ok(new TaskListResponse
        {
            Tasks = taskResponses,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
            HasNextPage = result.HasNextPage,
            HasPreviousPage = result.HasPreviousPage
        });
    }

    /// <summary>
    /// Dohvata statistiku zadataka.
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<TaskStatistics>> GetStatistics(CancellationToken cancellationToken)
    {
        var queued = await _taskRepository.GetTotalCountAsync(ProtocolTaskStatus.Queued, cancellationToken);
        var processing = await _taskRepository.GetTotalCountAsync(ProtocolTaskStatus.Processing, cancellationToken);
        var success = await _taskRepository.GetTotalCountAsync(ProtocolTaskStatus.Success, cancellationToken);
        var failed = await _taskRepository.GetTotalCountAsync(ProtocolTaskStatus.Failed, cancellationToken);

        return Ok(new TaskStatistics
        {
            TotalTasks = queued + processing + success + failed,
            QueuedTasks = queued,
            ProcessingTasks = processing,
            SuccessfulTasks = success,
            FailedTasks = failed
        });
    }

    private static TaskStatusResponse MapToResponse(ProtocolTask task, string? lastError)
    {
        return new TaskStatusResponse
        {
            TaskId = task.Id,
            DeviceName = task.DeviceName,
            Status = task.Status.ToString(),
            AttemptCount = task.AttemptCount,
            MaxAttempts = task.MaxAttempts,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            HasDriver = task.CurrentDriver != null,
            LastError = lastError
        };
    }
}
