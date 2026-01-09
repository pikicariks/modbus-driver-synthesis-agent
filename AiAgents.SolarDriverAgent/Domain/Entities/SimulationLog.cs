namespace AiAgents.SolarDriverAgent.Domain.Entities;

/// <summary>
/// Log simulacije - pamti rezultate testiranja drajvera.
/// Koristi se za LEARN fazu agenta.
/// </summary>
public class SimulationLog
{
    public Guid Id { get; private set; }

    /// <summary>
    /// ID zadatka za koji je simulacija izvršena.
    /// </summary>
    public Guid ProtocolTaskId { get; private set; }

    /// <summary>
    /// Broj pokušaja u kojem je simulacija izvršena.
    /// </summary>
    public int AttemptNumber { get; private set; }

    /// <summary>
    /// Da li je simulacija uspjela.
    /// </summary>
    public bool IsSuccess { get; private set; }

    /// <summary>
    /// Poruka greške (ako simulacija nije uspjela).
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Detaljan stack trace greške.
    /// </summary>
    public string? StackTrace { get; private set; }

    /// <summary>
    /// Modbus registri koji su testirani.
    /// </summary>
    public string? TestedRegisters { get; private set; }

    /// <summary>
    /// Očekivane vrijednosti.
    /// </summary>
    public string? ExpectedValues { get; private set; }

    /// <summary>
    /// Stvarne vrijednosti dobijene simulacijom.
    /// </summary>
    public string? ActualValues { get; private set; }

    /// <summary>
    /// Vrijeme izvršenja simulacije.
    /// </summary>
    public DateTime ExecutedAt { get; private set; }

    /// <summary>
    /// Trajanje simulacije u milisekundama.
    /// </summary>
    public long DurationMs { get; private set; }

    /// <summary>
    /// JSON sa internim pokušajima Python agenata.
    /// Format: [{attemptNumber, agentName, action, success, errorMessage, durationMs}]
    /// </summary>
    public string? InternalAttemptsJson { get; private set; }
    
    /// <summary>
    /// Broj internih pokušaja Python multi-agent sistema.
    /// </summary>
    public int InternalAttemptCount { get; private set; }
    
    /// <summary>
    /// Confidence score iz Python servisa.
    /// </summary>
    public double ConfidenceScore { get; private set; }

    private SimulationLog() { } // EF Core

    public static SimulationLog CreateSuccess(
        Guid protocolTaskId,
        int attemptNumber,
        string? testedRegisters = null,
        long durationMs = 0,
        int internalAttemptCount = 0,
        double confidenceScore = 0,
        string? internalAttemptsJson = null)
    {
        return new SimulationLog
        {
            Id = Guid.NewGuid(),
            ProtocolTaskId = protocolTaskId,
            AttemptNumber = attemptNumber,
            IsSuccess = true,
            TestedRegisters = testedRegisters,
            ExecutedAt = DateTime.UtcNow,
            DurationMs = durationMs,
            InternalAttemptCount = internalAttemptCount,
            ConfidenceScore = confidenceScore,
            InternalAttemptsJson = internalAttemptsJson
        };
    }

    public static SimulationLog CreateFailure(
        Guid protocolTaskId,
        int attemptNumber,
        string errorMessage,
        string? stackTrace = null,
        string? expectedValues = null,
        string? actualValues = null,
        int internalAttemptCount = 0,
        double confidenceScore = 0,
        string? internalAttemptsJson = null)
    {
        return new SimulationLog
        {
            Id = Guid.NewGuid(),
            ProtocolTaskId = protocolTaskId,
            AttemptNumber = attemptNumber,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            StackTrace = stackTrace,
            ExpectedValues = expectedValues,
            ActualValues = actualValues,
            ExecutedAt = DateTime.UtcNow,
            InternalAttemptCount = internalAttemptCount,
            ConfidenceScore = confidenceScore,
            InternalAttemptsJson = internalAttemptsJson
        };
    }

    /// <summary>
    /// Formatira log za slanje LLM-u kao kontekst.
    /// </summary>
    public string ToLlmContext()
    {
        if (IsSuccess)
            return $"[Attempt {AttemptNumber}] SUCCESS - Tested registers: {TestedRegisters}";

        return $"""
            [Attempt {AttemptNumber}] FAILED
            Error: {ErrorMessage}
            Expected: {ExpectedValues ?? "N/A"}
            Actual: {ActualValues ?? "N/A"}
            Stack Trace: {StackTrace ?? "N/A"}
            """;
    }
}
