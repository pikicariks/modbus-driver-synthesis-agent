using System.ComponentModel.DataAnnotations;

namespace AiAgents.SolarDriverAgent.Web.Models;

/// <summary>
/// Request model za upload PDF dokumentacije.
/// </summary>
public record UploadTaskRequest
{
    /// <summary>
    /// Naziv uređaja/invertera.
    /// </summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string DeviceName { get; init; } = string.Empty;
}

/// <summary>
/// Response nakon uspješnog uploada.
/// </summary>
public record UploadTaskResponse
{
    public Guid TaskId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public PdfInfo? PdfInfo { get; init; }
}

/// <summary>
/// Informacije o PDF dokumentu.
/// </summary>
public record PdfInfo
{
    public int PageCount { get; init; }
    public long FileSizeBytes { get; init; }
    public string? PdfVersion { get; init; }
    public bool HasExtractableText { get; init; }
}

/// <summary>
/// Response za status zadatka.
/// </summary>
public record TaskStatusResponse
{
    public Guid TaskId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool HasDriver { get; init; }
    public string? LastError { get; init; }
}

/// <summary>
/// Response za listu zadataka sa paginacijom.
/// </summary>
public record TaskListResponse
{
    public IReadOnlyList<TaskStatusResponse> Tasks { get; init; } = Array.Empty<TaskStatusResponse>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
}

/// <summary>
/// Statistika zadataka.
/// </summary>
public record TaskStatistics
{
    public int TotalTasks { get; init; }
    public int QueuedTasks { get; init; }
    public int ProcessingTasks { get; init; }
    public int SuccessfulTasks { get; init; }
    public int FailedTasks { get; init; }
}
