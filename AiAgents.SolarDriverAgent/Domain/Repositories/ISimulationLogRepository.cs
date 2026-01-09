using AiAgents.SolarDriverAgent.Domain.Entities;

namespace AiAgents.SolarDriverAgent.Domain.Repositories;

/// <summary>
/// Repository za pristup SimulationLog entitetima.
/// </summary>
public interface ISimulationLogRepository
{
    /// <summary>
    /// Dohvata sve logove za određeni zadatak.
    /// </summary>
    Task<IReadOnlyList<SimulationLog>> GetByTaskIdAsync(
        Guid protocolTaskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dohvata posljednjih N neuspjelih logova za zadatak.
    /// Koristi se za LEARN fazu - kontekst za LLM.
    /// </summary>
    Task<IReadOnlyList<SimulationLog>> GetRecentFailuresAsync(
        Guid protocolTaskId,
        int count = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dohvata posljednjih N neuspjelih logova globalno (za druge zadatke).
    /// Koristi se u SENSE fazi za RAG-like kontekst sličnih problema.
    /// </summary>
    Task<IReadOnlyList<SimulationLog>> GetRecentFailuresGlobalAsync(
        int count = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dodaje novi log.
    /// </summary>
    Task AddAsync(SimulationLog log, CancellationToken cancellationToken = default);
}
