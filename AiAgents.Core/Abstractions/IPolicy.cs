namespace AiAgents.Core.Abstractions;

/// <summary>
/// Politika odlučivanja za agenta - THINK faza.
/// Mapira percepciju na akciju.
/// </summary>
/// <typeparam name="TPercept">Tip ulazne percepcije</typeparam>
/// <typeparam name="TAction">Tip izlazne akcije</typeparam>
public interface IPolicy<TPercept, TAction>
    where TPercept : class
    where TAction : class
{
    /// <summary>
    /// Odlučuje koju akciju preduzeti na osnovu percepcije.
    /// </summary>
    Task<TAction> DecideAsync(TPercept percept, CancellationToken cancellationToken = default);
}
