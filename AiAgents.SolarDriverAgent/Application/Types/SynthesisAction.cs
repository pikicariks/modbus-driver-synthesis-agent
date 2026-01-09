namespace AiAgents.SolarDriverAgent.Application.Types;

/// <summary>
/// Akcija koju agent preduzima - zahtjev za sintezu.
/// </summary>
public class SynthesisAction
{
    /// <summary>
    /// ID zadatka.
    /// </summary>
    public required Guid TaskId { get; init; }

    /// <summary>
    /// Ekstraktovani tekst protokola.
    /// </summary>
    public required string ProtocolText { get; init; }

    /// <summary>
    /// Naziv uređaja.
    /// </summary>
    public required string DeviceName { get; init; }

    /// <summary>
    /// Kontekst prethodnih iskustava (za RAG).
    /// </summary>
    public string? ExperienceContext { get; init; }

    /// <summary>
    /// Ciljni jezik.
    /// </summary>
    public string TargetLanguage { get; init; } = "python";

    /// <summary>
    /// Broj pokušaja.
    /// </summary>
    public int AttemptNumber { get; init; }
}
