namespace AiAgents.SolarDriverAgent.Infrastructure.External;

/// <summary>
/// Konfiguracija za LLM klijent.
/// </summary>
public class LlmClientOptions
{
    public const string SectionName = "LlmService";

    /// <summary>
    /// Base URL Python/FastAPI servisa.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Timeout za HTTP zahtjeve u sekundama.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// API ključ za autentifikaciju.
    /// </summary>
    public string? ApiKey { get; set; }
}
