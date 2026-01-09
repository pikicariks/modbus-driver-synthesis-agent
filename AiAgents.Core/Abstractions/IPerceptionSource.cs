namespace AiAgents.Core.Abstractions;

/// <summary>
/// Izvor percepcije za agenta - SENSE faza.
/// Omogućava agentu da "vidi" svoje okruženje.
/// </summary>
/// <typeparam name="TPercept">Tip podataka koje agent percipira</typeparam>
public interface IPerceptionSource<TPercept>
    where TPercept : class
{
    /// <summary>
    /// Prikuplja trenutnu percepciju iz okruženja.
    /// Vraća null ako nema ničeg za percipirati (no-work scenario).
    /// </summary>
    Task<TPercept?> PerceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Provjerava da li ima posla bez dohvatanja percepta.
    /// Koristi se za optimizaciju - ne troši resurse ako nema posla.
    /// </summary>
    Task<bool> HasPendingWorkAsync(CancellationToken cancellationToken = default);
}
