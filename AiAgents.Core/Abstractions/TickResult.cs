namespace AiAgents.Core.Abstractions;

/// <summary>
/// Bazni rezultat jednog tika (iteracije) agenta.
/// Nasljeđuje se za specifične agente da nose bogate DTO informacije.
/// </summary>
public abstract class TickResult
{
    /// <summary>
    /// Da li je tik obradio neki posao.
    /// </summary>
    public bool DidWork { get; init; }

    /// <summary>
    /// Da li je došlo do greške.
    /// </summary>
    public bool HasError { get; init; }

    /// <summary>
    /// Opciona poruka o rezultatu.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Detalji greške ako postoji.
    /// </summary>
    public string? ErrorDetails { get; init; }

    /// <summary>
    /// Trajanje tika u milisekundama.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Timestamp kada je tik završen.
    /// </summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Generički rezultat tika sa tipiziranim podacima.
/// TData nosi specifične informacije za tip agenta.
/// </summary>
/// <typeparam name="TData">Tip podataka specifičnih za agenta</typeparam>
public class TickResult<TData> : TickResult
    where TData : class
{
    /// <summary>
    /// Podaci specifični za tip agenta (npr. SynthesisTickData, ScoringTickData).
    /// </summary>
    public TData? Data { get; init; }

    /// <summary>
    /// Kreira rezultat kada agent nije imao posla.
    /// </summary>
    public static TickResult<TData> Idle() => new()
    {
        DidWork = false
    };

    /// <summary>
    /// Kreira rezultat uspješno obavljenog posla.
    /// </summary>
    public static TickResult<TData> Success(TData data, string? message = null, long durationMs = 0) => new()
    {
        DidWork = true,
        Data = data,
        Message = message,
        DurationMs = durationMs
    };

    /// <summary>
    /// Kreira rezultat neuspjelog posla.
    /// </summary>
    public static TickResult<TData> Failure(string errorDetails, TData? partialData = null, long durationMs = 0) => new()
    {
        DidWork = true,
        HasError = true,
        ErrorDetails = errorDetails,
        Data = partialData,
        DurationMs = durationMs
    };
}
