namespace AiAgents.SolarDriverAgent.Domain.Enums;

/// <summary>
/// Status obrade protokol zadatka.
/// </summary>
public enum ProtocolTaskStatus
{
    /// <summary>
    /// Zadatak čeka na obradu.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Zadatak se trenutno obrađuje.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Drajver je uspješno generisan i validiran.
    /// </summary>
    Success = 2,

    /// <summary>
    /// Generisanje ili validacija nije uspjela.
    /// Zadatak će biti ponovo obrađen sa kontekstom greške.
    /// </summary>
    Failed = 3
}
