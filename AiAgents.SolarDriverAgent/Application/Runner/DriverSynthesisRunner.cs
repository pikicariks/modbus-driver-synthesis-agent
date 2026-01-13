using System.Diagnostics;
using System.Text.Json;
using AiAgents.Core;
using AiAgents.Core.Abstractions;
using AiAgents.SolarDriverAgent.Application.Contracts;
using AiAgents.SolarDriverAgent.Application.Percepts;
using AiAgents.SolarDriverAgent.Application.Types;
using AiAgents.SolarDriverAgent.Domain.Entities;
using AiAgents.SolarDriverAgent.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace AiAgents.SolarDriverAgent.Application.Runner;

/// <summary>
/// Glavni agent za sintezu Modbus drajvera.
/// Implementira Sense-Think-Act-Learn ciklus sa integracijom Python multi-agent sistema.
/// 
/// Generički parametri:
/// - TPercept = TaskPercept (zadatak + protokol tekst + kontekst iskustava)
/// - TAction = SynthesisAction (zahtjev za sintezu)
/// - TResult = SynthesisResult (rezultat iz Python servisa)
/// - TTickData = SynthesisTickData (bogat DTO za host emit)
/// </summary>
public class DriverSynthesisRunner : SoftwareAgent<TaskPercept, SynthesisAction, SynthesisResult, SynthesisTickData>
{
    private readonly IProtocolTaskRepository _taskRepository;
    private readonly ISimulationLogRepository _logRepository;
    private readonly IAgentSynthesisClient _synthesisClient;
    private readonly IAgentServiceHealthCheck _healthCheck;
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly ILogger<DriverSynthesisRunner> _logger;

    public DriverSynthesisRunner(
        IProtocolTaskRepository taskRepository,
        ISimulationLogRepository logRepository,
        IAgentSynthesisClient synthesisClient,
        IAgentServiceHealthCheck healthCheck,
        IPdfTextExtractor pdfExtractor,
        ILogger<DriverSynthesisRunner> logger)
        : base("driver-synthesis-agent", "Solar Driver Synthesis Agent")
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
        _synthesisClient = synthesisClient ?? throw new ArgumentNullException(nameof(synthesisClient));
        _healthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        _pdfExtractor = pdfExtractor ?? throw new ArgumentNullException(nameof(pdfExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Provjerava da li ima posla za obraditi.
    /// Uključuje provjeru da li je Python servis dostupan.
    /// </summary>
    public override async Task<bool> HasPendingWorkAsync(CancellationToken cancellationToken = default)
    {
        var hasTasks = await _taskRepository.HasEligibleTasksAsync(cancellationToken);
        if (!hasTasks)
            return false;

        var health = await _healthCheck.CheckHealthAsync(cancellationToken);
        if (!health.IsHealthy)
        {
            _logger.LogWarning(
                "Python agent service is not available: {Error}. Skipping tick.",
                health.ErrorDetails);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Izvršava jedan tik agenta - atomarna operacija.
    /// Sense → Think → Act → Learn
    /// </summary>
    public override async Task<TickResult<SynthesisTickData>> StepAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var health = await _healthCheck.CheckHealthAsync(cancellationToken);
        if (!health.IsHealthy)
        {
            _logger.LogWarning(
                "Python agent service unavailable: {Error}. Returning Idle.",
                health.ErrorDetails);
            return TickResult<SynthesisTickData>.Idle();
        }

        TaskPercept? percept;
        try
        {
            percept = await SenseAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("PDF"))
        {
            _logger.LogError(ex, "SENSE failed: PDF extraction error");
            stopwatch.Stop();
            
            var failedTask = await _taskRepository.GetNextEligibleTaskAsync(cancellationToken);
            if (failedTask != null)
            {
                failedTask.MarkAsProcessing();
                failedTask.MarkAsFailed($"PDF extraction failed: {ex.Message}");
                await _taskRepository.UpdateAsync(failedTask, cancellationToken);
                
                return TickResult<SynthesisTickData>.Failure(
                    $"SENSE failed: {ex.Message}",
                    CreatePartialTickData(failedTask, null),
                    stopwatch.ElapsedMilliseconds);
            }
            
            return TickResult<SynthesisTickData>.Failure(
                $"SENSE failed: {ex.Message}",
                null,
                stopwatch.ElapsedMilliseconds);
        }

        if (percept is null)
        {
            _logger.LogDebug("No eligible tasks found. Agent idle.");
            return TickResult<SynthesisTickData>.Idle();
        }

        var task = percept.Task;
        _logger.LogInformation(
            "SENSE complete for task {TaskId} ({DeviceName}). " +
            "Protocol text: {TextLength} chars, Attempt: {Attempt}",
            task.Id, task.DeviceName, percept.ProtocolText.Length, task.AttemptCount + 1);

        try
        {
            task.MarkAsProcessing();
            await _taskRepository.UpdateAsync(task, cancellationToken);

            var action = await ThinkAsync(percept, cancellationToken);
            if (action is null)
            {
                task.MarkAsFailed("THINK failed: Could not create synthesis action");
                await _taskRepository.UpdateAsync(task, cancellationToken);

                stopwatch.Stop();
                return TickResult<SynthesisTickData>.Failure(
                    "THINK failed: Could not create synthesis action",
                    CreatePartialTickData(task, null),
                    stopwatch.ElapsedMilliseconds);
            }

            _logger.LogDebug(
                "THINK complete for task {TaskId}. Action: synthesize {Language} driver",
                task.Id, action.TargetLanguage);

            var result = await ActAsync(action, cancellationToken);

            if (!result.Success && IsInfrastructureError(result.ErrorMessage))
            {
                _logger.LogWarning(
                    "ACT failed due to infrastructure error: {Error}. Reverting to Queued.",
                    result.ErrorMessage);

                task.RevertToQueued();
                await _taskRepository.UpdateAsync(task, cancellationToken);

                stopwatch.Stop();
                return TickResult<SynthesisTickData>.Idle();
            }

            await LearnAsync(percept, action, result, cancellationToken);

            stopwatch.Stop();

            if (result.Success)
            {
                var driver = DriverCode.Create(
                    task.Id,
                    result.DriverCode!,
                    result.TotalInternalAttempts);
                driver.MarkAsValidated();
                task.MarkAsSuccess(driver);
                await _taskRepository.UpdateAsync(task, cancellationToken);

                _logger.LogInformation(
                    "Task {TaskId} SUCCESS. Confidence: {Confidence:P0}, " +
                    "Internal attempts: {Attempts}, Duration: {Duration}ms",
                    task.Id, result.ConfidenceScore, result.TotalInternalAttempts,
                    stopwatch.ElapsedMilliseconds);

                var tickData = CreateTickData(percept, action, result);
                return TickResult<SynthesisTickData>.Success(
                    tickData,
                    $"Driver generated for {task.DeviceName} with {result.ConfidenceScore:P0} confidence",
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                task.MarkAsFailed(result.ErrorMessage ?? "Unknown synthesis error");
                await _taskRepository.UpdateAsync(task, cancellationToken);

                if (task.CanRetry())
                {
                    _logger.LogWarning(
                        "Task {TaskId} FAILED, will retry ({Attempts}/{Max}). Error: {Error}",
                        task.Id, task.AttemptCount, task.MaxAttempts, result.ErrorMessage);
                }
                else
                {
                    _logger.LogError(
                        "Task {TaskId} FAILED permanently after {Attempts} attempts. Error: {Error}",
                        task.Id, task.AttemptCount, result.ErrorMessage);
                }

                var tickData = CreateTickData(percept, action, result);
                return TickResult<SynthesisTickData>.Failure(
                    result.ErrorMessage ?? "Synthesis failed",
                    tickData,
                    stopwatch.ElapsedMilliseconds);
            }
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "HTTP error during ACT for task {TaskId}: {Error}. Reverting to Queued.",
                task.Id, ex.Message);

            task.RevertToQueued();
            await _taskRepository.UpdateAsync(task, cancellationToken);

            return TickResult<SynthesisTickData>.Idle();
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            task.RevertToQueued();
            await _taskRepository.UpdateAsync(task, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error processing task {TaskId}", task.Id);
            
            var exceptionLog = SimulationLog.CreateFailure(
                task.Id, 
                task.AttemptCount, 
                $"Unexpected error: {ex.Message}",
                ex.StackTrace);
            await _logRepository.AddAsync(exceptionLog, cancellationToken);
            
            task.MarkAsFailed();
            await _taskRepository.UpdateAsync(task, cancellationToken);

            return TickResult<SynthesisTickData>.Failure(
                ex.Message,
                CreatePartialTickData(task, null),
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// SENSE: Prikuplja percepciju - sve što agent "vidi".
    /// - Dohvata zadatak iz baze
    /// - Ekstraktuje tekst iz PDF-a (agent "čita" dokument)
    /// - Dohvata prethodna iskustva za kontekst
    /// </summary>
    protected override async Task<TaskPercept?> SenseAsync(CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetNextEligibleTaskAsync(cancellationToken);
        if (task is null)
            return null;

        string protocolText;
        if (string.IsNullOrEmpty(task.ExtractedSpecification))
        {
            _logger.LogDebug("SENSE: Extracting text from PDF for task {TaskId}", task.Id);

            protocolText = await _pdfExtractor.ExtractTextAsync(task.PdfDocument, cancellationToken);

            task.SetExtractedSpecification(protocolText);
            await _taskRepository.UpdateAsync(task, cancellationToken);

            _logger.LogInformation(
                "SENSE: Extracted {Length} characters from PDF for task {TaskId}",
                protocolText.Length, task.Id);
        }
        else
        {
            protocolText = task.ExtractedSpecification;
            _logger.LogDebug(
                "SENSE: Using cached protocol text ({Length} chars) for task {TaskId}",
                protocolText.Length, task.Id);
        }

        var previousErrors = new List<string>();
        string? experienceContext = null;

        IReadOnlyList<SimulationLog> taskLogs = Array.Empty<SimulationLog>();

        if (task.AttemptCount > 0)
        {
            taskLogs = await _logRepository.GetRecentFailuresAsync(task.Id, 3, cancellationToken);
            previousErrors.AddRange(taskLogs.Select(l => l.ToLlmContext()));
            experienceContext = FormatExperienceContext(taskLogs, Array.Empty<SimulationLog>());

            _logger.LogDebug(
                "SENSE: Found {Count} previous failure logs for task {TaskId}",
                taskLogs.Count, task.Id);
        }

        var globalFailures = await _logRepository.GetRecentFailuresGlobalAsync(3, cancellationToken);
        if (globalFailures.Count > 0)
        {
            experienceContext = FormatExperienceContext(
                taskLogs,
                globalFailures);

            _logger.LogDebug(
                "SENSE: Added {Count} global failure logs for cross-task context",
                globalFailures.Count);
        }

        return new TaskPercept
        {
            Task = task,
            ProtocolText = protocolText,
            PreviousErrors = previousErrors,
            ExperienceContext = experienceContext
        };
    }

    /// <summary>
    /// THINK: Kreira akciju za sintezu na osnovu percepta.
    /// Koristi protokol tekst iz SENSE faze.
    /// </summary>
    protected override Task<SynthesisAction?> ThinkAsync(TaskPercept percept, CancellationToken cancellationToken)
    {
        if (!percept.HasValidProtocolText)
        {
            _logger.LogWarning(
                "THINK: Invalid protocol text for task {TaskId}. Text: '{Preview}...'",
                percept.Task.Id,
                percept.ProtocolText.Length > 50 ? percept.ProtocolText[..50] : percept.ProtocolText);
            return Task.FromResult<SynthesisAction?>(null);
        }

        var action = new SynthesisAction
        {
            TaskId = percept.Task.Id,
            ProtocolText = percept.ProtocolText,
            DeviceName = percept.Task.DeviceName,
            ExperienceContext = percept.ExperienceContext,
            TargetLanguage = "python",
            AttemptNumber = percept.Task.AttemptCount
        };

        return Task.FromResult<SynthesisAction?>(action);
    }

    /// <summary>
    /// ACT: Izvršava akciju - poziva Python multi-agent sistem.
    /// </summary>
    protected override async Task<SynthesisResult> ActAsync(SynthesisAction action, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "ACT: Calling Python synthesis service for task {TaskId} ({TextLength} chars)",
            action.TaskId, action.ProtocolText.Length);

        var request = new SynthesisRequest
        {
            ProtocolText = action.ProtocolText,
            DeviceName = action.DeviceName,
            TargetLanguage = action.TargetLanguage,
            PreviousExperience = action.ExperienceContext
        };

        return await _synthesisClient.SynthesizeDriverAsync(request, cancellationToken);
    }

    /// <summary>
    /// LEARN: Uči iz rezultata - snima iskustvo u bazu.
    /// Uključuje sve interne pokušaje Python agenata.
    /// </summary>
    protected override async Task LearnAsync(
        TaskPercept percept,
        SynthesisAction action,
        SynthesisResult result,
        CancellationToken cancellationToken)
    {
        var task = percept.Task;

        string? internalAttemptsJson = null;
        if (result.InternalAttempts.Count > 0)
        {
            var attemptData = result.InternalAttempts.Select(a => new
            {
                attemptNumber = a.AttemptNumber,
                agentName = a.AgentName,
                action = a.Action,
                success = a.Success,
                errorMessage = a.ErrorMessage,
                durationMs = a.DurationMs,
                timestamp = a.Timestamp.ToString("HH:mm:ss")
            });
            internalAttemptsJson = JsonSerializer.Serialize(attemptData);
        }

        SimulationLog log;
        if (result.Success)
        {
            log = SimulationLog.CreateSuccess(
                task.Id,
                task.AttemptCount,
                string.Join(", ", result.TestResult?.TestedRegisters ?? Array.Empty<string>()),
                0,
                result.TotalInternalAttempts,
                result.ConfidenceScore,
                internalAttemptsJson);

            _logger.LogDebug(
                "LEARN: Recording SUCCESS for task {TaskId}. Tested: [{Registers}], InternalAttempts: {Attempts}, Confidence: {Confidence:P0}",
                task.Id,
                string.Join(", ", result.TestResult?.TestedRegisters ?? Array.Empty<string>()),
                result.TotalInternalAttempts,
                result.ConfidenceScore);
        }
        else
        {
            log = SimulationLog.CreateFailure(
                task.Id,
                task.AttemptCount,
                result.TestResult?.ErrorMessage ?? result.ErrorMessage ?? "Unknown error",
                stackTrace: null,
                expectedValues: result.TestResult?.ExpectedBytes,
                actualValues: result.TestResult?.ActualBytes,
                result.TotalInternalAttempts,
                result.ConfidenceScore,
                internalAttemptsJson);

            _logger.LogDebug(
                "LEARN: Recording FAILURE for task {TaskId}. Error: {Error}, BytePos: {BytePos}, InternalAttempts: {Attempts}",
                task.Id,
                result.TestResult?.ErrorMessage ?? result.ErrorMessage,
                result.TestResult?.ErrorBytePosition,
                result.TotalInternalAttempts);
        }

        await _logRepository.AddAsync(log, cancellationToken);

        foreach (var attempt in result.InternalAttempts)
        {
            _logger.LogDebug(
                "LEARN: Python [{Agent}] attempt {Num}: {Action} - {Status} ({Duration}ms)",
                attempt.AgentName, attempt.AttemptNumber, attempt.Action,
                attempt.Success ? "OK" : "FAIL", attempt.DurationMs);
        }

        if (!string.IsNullOrEmpty(result.ExperienceId))
        {
            _logger.LogInformation(
                "LEARN: Experience stored in ChromaDB: {ExperienceId}",
                result.ExperienceId);
        }
    }

    /// <summary>
    /// Provjerava da li je greška infrastrukturna.
    /// </summary>
    private static bool IsInfrastructureError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        var infraErrors = new[]
        {
            "connection refused", "connection failed", "network unreachable",
            "host unreachable", "timed out", "timeout",
            "503", "502", "504", "service unavailable"
        };

        var lowerError = errorMessage.ToLowerInvariant();
        return infraErrors.Any(e => lowerError.Contains(e));
    }

    /// <summary>
    /// Kreira bogat TickData iz komponenti ciklusa.
    /// </summary>
    protected override SynthesisTickData CreateTickData(TaskPercept percept, SynthesisAction action, SynthesisResult result)
    {
        var task = percept.Task;

        var errorMessage = result.ErrorMessage ?? result.TestResult?.ErrorMessage;

        return new SynthesisTickData
        {
            TaskId = task.Id,
            DeviceName = task.DeviceName,
            Status = task.Status.ToString(),
            ModelVersion = result.TotalInternalAttempts,
            InternalAttempts = result.TotalInternalAttempts,
            ConfidenceScore = result.ConfidenceScore,
            TotalAttempts = task.AttemptCount,
            MaxAttempts = task.MaxAttempts,
            SynthesisSuccess = result.Success,
            TestedRegisters = result.TestResult?.TestedRegisters ?? Array.Empty<string>(),
            ErrorMessage = errorMessage,
            ProblematicBytes = result.TestResult?.ActualBytes,
            BytePosition = result.TestResult?.ErrorBytePosition,
            ExperienceId = result.ExperienceId,
            AgentLogs = result.InternalAttempts.Select(a => new AgentAttemptInfo
            {
                AttemptNumber = a.AttemptNumber,
                AgentName = a.AgentName,
                Action = a.Action,
                Success = a.Success,
                ErrorMessage = a.ErrorMessage,
                DurationMs = a.DurationMs
            }).ToList()
        };
    }

    /// <summary>
    /// Kreira parcijalni TickData za slučaj greške.
    /// </summary>
    private static SynthesisTickData CreatePartialTickData(ProtocolTask task, SynthesisResult? result)
    {
        var errorMessage = result?.ErrorMessage ?? result?.TestResult?.ErrorMessage;
        
        return new SynthesisTickData
        {
            TaskId = task.Id,
            DeviceName = task.DeviceName,
            Status = task.Status.ToString(),
            TotalAttempts = task.AttemptCount,
            MaxAttempts = task.MaxAttempts,
            SynthesisSuccess = false,
            ErrorMessage = errorMessage,
            ConfidenceScore = result?.ConfidenceScore ?? 0,
            InternalAttempts = result?.TotalInternalAttempts ?? 0
        };
    }

    /// <summary>
    /// Formatira prethodna iskustva za slanje Python servisu.
    /// </summary>
    private static string? FormatExperienceContext(
        IReadOnlyList<SimulationLog> taskLogs,
        IReadOnlyList<SimulationLog> globalLogs)
    {
        if (taskLogs.Count == 0 && globalLogs.Count == 0)
            return null;

        var parts = new List<string>();

        if (taskLogs.Count > 0)
        {
            parts.Add("## Previous Attempts for This Task:");
            parts.AddRange(taskLogs.Select(l => l.ToLlmContext()));
        }

        if (globalLogs.Count > 0)
        {
            parts.Add("## Similar Failures Across Other Tasks (recent):");
            parts.AddRange(globalLogs.Select(l => l.ToLlmContext()));
        }

        return string.Join("\n\n", parts);
    }
}
