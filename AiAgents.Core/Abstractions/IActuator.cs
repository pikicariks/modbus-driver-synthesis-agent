namespace AiAgents.Core.Abstractions;

/// <summary>
/// Aktuator koji izvršava akcije - ACT faza.
/// Omogućava agentu da djeluje na okruženje.
/// </summary>
/// <typeparam name="TAction">Tip akcije za izvršenje</typeparam>
/// <typeparam name="TResult">Tip rezultata akcije</typeparam>
public interface IActuator<TAction, TResult>
    where TAction : class
    where TResult : class
{
    /// <summary>
    /// Izvršava akciju i vraća rezultat.
    /// </summary>
    Task<TResult> ExecuteAsync(TAction action, CancellationToken cancellationToken = default);
}
