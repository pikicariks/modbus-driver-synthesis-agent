using AiAgents.SolarDriverAgent.Domain.Entities;
using AiAgents.SolarDriverAgent.Domain.Enums;

namespace AiAgents.SolarDriverAgent.Domain.Repositories;

/// <summary>
/// Repository za upravljanje ProtocolTask entitetima.
/// </summary>
public interface IProtocolTaskRepository
{
    /// <summary>
    /// Dohvata sljedeći zadatak spreman za obradu.
    /// Vraća Queued ili Failed (koji može retry) zadatak.
    /// </summary>
    Task<ProtocolTask?> GetNextEligibleTaskAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dohvata zadatak po ID-u.
    /// </summary>
    Task<ProtocolTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provjerava da li postoji bilo koji zadatak spreman za obradu.
    /// </summary>
    Task<bool> HasEligibleTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dohvata sve zadatke sa paginacijom.
    /// </summary>
    Task<PagedResult<ProtocolTask>> GetAllAsync(
        int page = 1,
        int pageSize = 20,
        ProtocolTaskStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dohvata ukupan broj zadataka.
    /// </summary>
    Task<int> GetTotalCountAsync(
        ProtocolTaskStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dodaje novi zadatak.
    /// </summary>
    Task AddAsync(ProtocolTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ažurira postojeći zadatak.
    /// </summary>
    Task UpdateAsync(ProtocolTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprema promjene u bazu.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Rezultat sa paginacijom.
/// </summary>
public record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
