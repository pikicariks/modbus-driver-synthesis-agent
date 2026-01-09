namespace AiAgents.SolarDriverAgent.Web.Hubs;

/// <summary>
/// Strongly-typed SignalR client interface.
/// Definira metode koje server može pozvati na klijentu.
/// </summary>
public interface IAgentHubClient
{
    /// <summary>
    /// Šalje rezultat tika klijentima.
    /// </summary>
    Task TickCompleted(TickNotification notification);

    /// <summary>
    /// Šalje status agenta (idle/working).
    /// </summary>
    Task AgentStatusChanged(AgentStatusNotification status);

    /// <summary>
    /// Šalje poruku kada se kreira novi task.
    /// </summary>
    Task TaskCreated(TaskCreatedNotification notification);

    /// <summary>
    /// Odgovor na RequestStatus.
    /// </summary>
    Task StatusRequested(DateTime timestamp);
    
    /// <summary>
    /// Real-time log procesiranja - šalje svaku fazu/korak.
    /// </summary>
    Task ProcessingLog(ProcessingLogEntry entry);
}

public record TaskCreatedNotification
{
    public Guid TaskId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public int PageCount { get; init; }
    public long FileSizeBytes { get; init; }
}

/// <summary>
/// Real-time log entry za praćenje procesiranja.
/// </summary>
public record ProcessingLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid TaskId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    
    /// <summary>
    /// SENSE, THINK, ACT, LEARN, PYTHON_PARSER, PYTHON_CODER, PYTHON_TESTER
    /// </summary>
    public string Phase { get; init; } = string.Empty;
    
    /// <summary>
    /// Started, InProgress, Completed, Failed
    /// </summary>
    public string Status { get; init; } = "InProgress";
    
    public string? Message { get; init; }
    public string? Details { get; init; }
    public int? AttemptNumber { get; init; }
    public long? DurationMs { get; init; }
}
