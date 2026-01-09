using AiAgents.Core.Abstractions;

namespace AiAgents.Core;

/// <summary>
/// Bazna klasa za sve softverske agente.
/// Implementira Sense-Think-Act-Learn ciklus sa generičkim tipovima.
/// </summary>
/// <typeparam name="TPercept">Tip percepcije (šta agent "vidi")</typeparam>
/// <typeparam name="TAction">Tip akcije (šta agent "radi")</typeparam>
/// <typeparam name="TResult">Tip rezultata akcije</typeparam>
/// <typeparam name="TTickData">Tip podataka u TickResult (bogat DTO za host)</typeparam>
public abstract class SoftwareAgent<TPercept, TAction, TResult, TTickData>
    where TPercept : class
    where TAction : class
    where TResult : class
    where TTickData : class
{
    /// <summary>
    /// Jedinstveni identifikator agenta.
    /// </summary>
    public string AgentId { get; }

    /// <summary>
    /// Ime agenta za logging.
    /// </summary>
    public string Name { get; }

    protected SoftwareAgent(string agentId, string name)
    {
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Izvršava jedan tik (iteraciju) agenta.
    /// Atomarna operacija: Sense → Think → Act → Learn.
    /// Ne smije sadržavati Task.Delay ili SignalR logiku.
    /// </summary>
    /// <returns>Bogat TickResult sa TTickData koji host može emitovati</returns>
    public abstract Task<TickResult<TTickData>> StepAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Provjerava da li agent ima posla za obraditi.
    /// Koristi se za optimizaciju - ne troši resurse ako nema posla.
    /// </summary>
    public abstract Task<bool> HasPendingWorkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// SENSE faza: Prikuplja percepciju iz okruženja.
    /// </summary>
    protected abstract Task<TPercept?> SenseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// THINK faza: Odlučuje koju akciju preduzeti.
    /// </summary>
    protected abstract Task<TAction?> ThinkAsync(TPercept percept, CancellationToken cancellationToken);

    /// <summary>
    /// ACT faza: Izvršava akciju.
    /// </summary>
    protected abstract Task<TResult> ActAsync(TAction action, CancellationToken cancellationToken);

    /// <summary>
    /// LEARN faza: Uči iz rezultata (opcionalno).
    /// </summary>
    protected virtual Task LearnAsync(TPercept percept, TAction action, TResult result, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kreira TickData iz komponenti ciklusa.
    /// Svaki agent implementira kako mapira rezultate u bogat DTO.
    /// </summary>
    protected abstract TTickData CreateTickData(TPercept percept, TAction action, TResult result);
}
