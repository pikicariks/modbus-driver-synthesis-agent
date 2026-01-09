namespace AiAgents.SolarDriverAgent.Application.Contracts;

/// <summary>
/// Klijent za komunikaciju sa Python multi-agent servisom.
/// Ovaj interfejs zamjenjuje stari ILlmClient sa bogatijim API-jem.
/// </summary>
public interface IAgentSynthesisClient
{
    /// <summary>
    /// Sintetiše Modbus drajver koristeći Python multi-agent sistem.
    /// Interno koristi LangGraph sa Parser, Coder i Tester agentima.
    /// </summary>
    Task<SynthesisResult> SynthesizeDriverAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Zahtjev za sintezu drajvera.
/// </summary>
public record SynthesisRequest
{
    /// <summary>
    /// Ekstraktovani tekst iz PDF protokola.
    /// </summary>
    public required string ProtocolText { get; init; }

    /// <summary>
    /// Kontekst prethodnih iskustava (iz ChromaDB).
    /// </summary>
    public string? PreviousExperience { get; init; }

    /// <summary>
    /// Ciljni programski jezik.
    /// </summary>
    public string TargetLanguage { get; init; } = "python";

    /// <summary>
    /// Naziv uređaja.
    /// </summary>
    public string? DeviceName { get; init; }
}

/// <summary>
/// Rezultat sinteze drajvera.
/// </summary>
public record SynthesisResult
{
    /// <summary>
    /// Da li je sinteza uspješna.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Generisani kod drajvera.
    /// </summary>
    public string? DriverCode { get; init; }

    /// <summary>
    /// Ciljni jezik.
    /// </summary>
    public string TargetLanguage { get; init; } = "python";

    /// <summary>
    /// Procjena pouzdanosti (0.0 - 1.0).
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <summary>
    /// Ukupan broj internih pokušaja u Python servisu.
    /// </summary>
    public int TotalInternalAttempts { get; init; }

    /// <summary>
    /// Logovi internih pokušaja.
    /// </summary>
    public IReadOnlyList<InternalAttemptLog> InternalAttempts { get; init; } = Array.Empty<InternalAttemptLog>();

    /// <summary>
    /// Rezultat Modbus testa.
    /// </summary>
    public ModbusTestResult? TestResult { get; init; }

    /// <summary>
    /// Ekstraktovani registri.
    /// </summary>
    public IReadOnlyList<ExtractedRegister> ExtractedRegisters { get; init; } = Array.Empty<ExtractedRegister>();

    /// <summary>
    /// Poruka greške ako sinteza nije uspjela.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// ID iskustva u ChromaDB.
    /// </summary>
    public string? ExperienceId { get; init; }
}

/// <summary>
/// Log jednog internog pokušaja.
/// </summary>
public record InternalAttemptLog
{
    public int AttemptNumber { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int DurationMs { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Rezultat Modbus testa.
/// </summary>
public record ModbusTestResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> TestedRegisters { get; init; } = Array.Empty<string>();
    public string? ExpectedBytes { get; init; }
    public string? ActualBytes { get; init; }
    public string? ErrorMessage { get; init; }
    public int? ErrorBytePosition { get; init; }
}

/// <summary>
/// Ekstraktovani registar iz specifikacije.
/// </summary>
public record ExtractedRegister
{
    public int Address { get; init; }
    public string AddressHex { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = "uint16";
    public int FunctionCode { get; init; } = 3;
}
