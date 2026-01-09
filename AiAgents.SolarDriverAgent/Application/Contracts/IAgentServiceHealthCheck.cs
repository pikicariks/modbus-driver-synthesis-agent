namespace AiAgents.SolarDriverAgent.Application.Contracts;

/// <summary>
/// Health check za Python agent servis.
/// </summary>
public interface IAgentServiceHealthCheck
{
    /// <summary>
    /// Provjerava da li je Python servis dostupan.
    /// </summary>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Rezultat health check-a.
/// </summary>
public record HealthCheckResult
{
    /// <summary>
    /// Da li je servis zdrav/dostupan.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Poruka o statusu.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Vrijeme odziva u ms.
    /// </summary>
    public long ResponseTimeMs { get; init; }

    /// <summary>
    /// Timestamp provjere.
    /// </summary>
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Detalji greške ako servis nije zdrav.
    /// </summary>
    public string? ErrorDetails { get; init; }

    public static HealthCheckResult Healthy(long responseTimeMs) => new()
    {
        IsHealthy = true,
        Message = "Service is healthy",
        ResponseTimeMs = responseTimeMs
    };

    public static HealthCheckResult Unhealthy(string error, long responseTimeMs = 0) => new()
    {
        IsHealthy = false,
        Message = "Service is unhealthy",
        ErrorDetails = error,
        ResponseTimeMs = responseTimeMs
    };
}
