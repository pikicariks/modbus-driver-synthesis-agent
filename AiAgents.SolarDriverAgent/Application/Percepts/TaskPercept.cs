using AiAgents.SolarDriverAgent.Domain.Entities;

namespace AiAgents.SolarDriverAgent.Application.Percepts;

/// <summary>
/// Percepcija agenta - sve što agent "vidi" u jednom tiku.
/// Uključuje zadatak, ekstraktovani protokol tekst, i kontekst iskustava.
/// </summary>
public class TaskPercept
{
    /// <summary>
    /// Zadatak za obradu.
    /// </summary>
    public required ProtocolTask Task { get; init; }

    /// <summary>
    /// Ekstraktovani tekst iz PDF protokola.
    /// Ovo je ono što agent "čita" - dio SENSE faze.
    /// </summary>
    public required string ProtocolText { get; init; }

    /// <summary>
    /// Prethodne greške za kontekst (LEARN feedback).
    /// </summary>
    public IReadOnlyList<string> PreviousErrors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Formatirani kontekst iskustava iz prethodnih pokušaja.
    /// Koristi se za RAG - agent "osjeća" sličnost sa prethodnim problemima.
    /// </summary>
    public string? ExperienceContext { get; init; }

    /// <summary>
    /// Da li je ovo retry pokušaj.
    /// </summary>
    public bool IsRetry => Task.AttemptCount > 0;

    /// <summary>
    /// Da li je PDF uspješno ekstraktovan.
    /// </summary>
    public bool HasValidProtocolText => !string.IsNullOrWhiteSpace(ProtocolText) &&
                                         !ProtocolText.StartsWith("[WARNING:");
}
