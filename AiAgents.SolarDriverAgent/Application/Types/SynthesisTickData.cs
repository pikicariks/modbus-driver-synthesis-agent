namespace AiAgents.SolarDriverAgent.Application.Types;

/// <summary>
/// Bogat DTO koji nosi sve informacije iz jednog tika sinteze.
/// Host može direktno emitovati ove podatke bez ponovnog računanja.
/// </summary>
public class SynthesisTickData
{
    /// <summary>
    /// ID zadatka koji je obrađen.
    /// </summary>
    public Guid TaskId { get; init; }

    /// <summary>
    /// Naziv uređaja.
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Status zadatka nakon tika.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Verzija generisanog koda/modela.
    /// </summary>
    public int ModelVersion { get; init; }

    /// <summary>
    /// Broj internih pokušaja u Python servisu.
    /// </summary>
    public int InternalAttempts { get; init; }

    /// <summary>
    /// Procjena pouzdanosti (0.0 - 1.0).
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// Ukupan broj pokušaja na .NET strani.
    /// </summary>
    public int TotalAttempts { get; init; }

    /// <summary>
    /// Maksimalan broj pokušaja.
    /// </summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// Da li je sinteza uspješna.
    /// </summary>
    public bool SynthesisSuccess { get; init; }

    /// <summary>
    /// Testirani Modbus registri.
    /// </summary>
    public IReadOnlyList<string> TestedRegisters { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Poruka greške (ako postoji).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Problematični bajtovi (za LEARN).
    /// </summary>
    public string? ProblematicBytes { get; init; }

    /// <summary>
    /// Pozicija problematičnog bajta.
    /// </summary>
    public int? BytePosition { get; init; }

    /// <summary>
    /// ID iskustva u ChromaDB.
    /// </summary>
    public string? ExperienceId { get; init; }

    /// <summary>
    /// Logovi internih agenata.
    /// </summary>
    public IReadOnlyList<AgentAttemptInfo> AgentLogs { get; init; } = Array.Empty<AgentAttemptInfo>();
}

/// <summary>
/// Info o pojedinačnom pokušaju internog agenta.
/// </summary>
public class AgentAttemptInfo
{
    public int AttemptNumber { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int DurationMs { get; init; }
}
