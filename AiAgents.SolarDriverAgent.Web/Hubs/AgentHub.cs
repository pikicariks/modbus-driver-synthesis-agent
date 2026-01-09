using AiAgents.SolarDriverAgent.Application.Types;
using Microsoft.AspNetCore.SignalR;

namespace AiAgents.SolarDriverAgent.Web.Hubs;

/// <summary>
/// SignalR Hub za real-time komunikaciju sa frontend-om.
/// Šalje TickResult podatke svim povezanim klijentima.
/// </summary>
public class AgentHub : Hub<IAgentHubClient>
{
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(ILogger<AgentHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Klijent može zatražiti trenutni status.
    /// </summary>
    public async Task RequestStatus()
    {
        _logger.LogDebug("Status requested by {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.StatusRequested(DateTime.UtcNow);
    }
}

/// <summary>
/// DTO za SignalR poruke - frontend-friendly format.
/// </summary>
public record TickNotification
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string EventType { get; init; } = "TickCompleted";
    
    // Task info
    public Guid TaskId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    
    // Result info
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }
    
    // Metrics
    public int InternalAttempts { get; init; }
    public double ConfidenceScore { get; init; }
    public int TotalAttempts { get; init; }
    public int MaxAttempts { get; init; }
    public long DurationMs { get; init; }
    
    // Extracted data
    public int RegisterCount { get; init; }
    public IReadOnlyList<string> TestedRegisters { get; init; } = Array.Empty<string>();
    
    // Learning
    public string? ExperienceId { get; init; }
    public string? ProblematicBytes { get; init; }
    public int? BytePosition { get; init; }
    
    // Agent logs
    public IReadOnlyList<AgentLogEntry> AgentLogs { get; init; } = Array.Empty<AgentLogEntry>();
}

public record AgentLogEntry
{
    public int AttemptNumber { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int DurationMs { get; init; }
}

/// <summary>
/// Notifikacija o statusu agenta (idle/working).
/// </summary>
public record AgentStatusNotification
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Status { get; init; } = "Idle"; // Idle, Working, Error
    public int PendingTasks { get; init; }
    public bool PythonServiceHealthy { get; init; }
}
