using AiAgents.SolarDriverAgent.Domain.Entities;
using AiAgents.SolarDriverAgent.Domain.Enums;
using AiAgents.SolarDriverAgent.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AiAgents.SolarDriverAgent.Infrastructure.Persistence;

/// <summary>
/// EF Core implementacija IProtocolTaskRepository.
/// </summary>
public class ProtocolTaskRepository : IProtocolTaskRepository
{
    private readonly AppDbContext _context;

    public ProtocolTaskRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ProtocolTask?> GetNextEligibleTaskAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProtocolTasks
            .Include(t => t.CurrentDriver)
            .Include(t => t.SimulationLogs)
            .Where(t => t.Status == ProtocolTaskStatus.Queued ||
                       (t.Status == ProtocolTaskStatus.Failed && t.AttemptCount < t.MaxAttempts))
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProtocolTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ProtocolTasks
            .Include(t => t.CurrentDriver)
            .Include(t => t.SimulationLogs)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<bool> HasEligibleTasksAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProtocolTasks
            .AnyAsync(t => t.Status == ProtocolTaskStatus.Queued ||
                          (t.Status == ProtocolTaskStatus.Failed && t.AttemptCount < t.MaxAttempts),
                      cancellationToken);
    }

    public async Task<PagedResult<ProtocolTask>> GetAllAsync(
        int page = 1,
        int pageSize = 20,
        ProtocolTaskStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.ProtocolTasks
            .Include(t => t.CurrentDriver)
            .Include(t => t.SimulationLogs)
            .AsNoTracking();

        if (statusFilter.HasValue)
        {
            query = query.Where(t => t.Status == statusFilter.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProtocolTask>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<int> GetTotalCountAsync(
        ProtocolTaskStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ProtocolTasks.AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(t => t.Status == statusFilter.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task AddAsync(ProtocolTask task, CancellationToken cancellationToken = default)
    {
        await _context.ProtocolTasks.AddAsync(task, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ProtocolTask task, CancellationToken cancellationToken = default)
    {
        if (task.CurrentDriver != null)
        {
            var driverEntry = _context.Entry(task.CurrentDriver);
            if (driverEntry.State == EntityState.Detached)
            {
                _context.Set<DriverCode>().Add(task.CurrentDriver);
            }
        }
        
        foreach (var log in task.SimulationLogs)
        {
            var logEntry = _context.Entry(log);
            if (logEntry.State == EntityState.Detached)
            {
                _context.Set<SimulationLog>().Add(log);
            }
        }
        
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
