using AiAgents.SolarDriverAgent.Domain.Entities;
using AiAgents.SolarDriverAgent.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AiAgents.SolarDriverAgent.Infrastructure.Persistence;

/// <summary>
/// EF Core implementacija ISimulationLogRepository.
/// </summary>
public class SimulationLogRepository : ISimulationLogRepository
{
    private readonly AppDbContext _context;

    public SimulationLogRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<SimulationLog>> GetByTaskIdAsync(
        Guid protocolTaskId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SimulationLogs
            .Where(l => l.ProtocolTaskId == protocolTaskId)
            .OrderByDescending(l => l.ExecutedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SimulationLog>> GetRecentFailuresAsync(
        Guid protocolTaskId,
        int count = 3,
        CancellationToken cancellationToken = default)
    {
        return await _context.SimulationLogs
            .Where(l => l.ProtocolTaskId == protocolTaskId && !l.IsSuccess)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SimulationLog>> GetRecentFailuresGlobalAsync(
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        return await _context.SimulationLogs
            .Where(l => !l.IsSuccess)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(count)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(SimulationLog log, CancellationToken cancellationToken = default)
    {
        await _context.SimulationLogs.AddAsync(log, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
