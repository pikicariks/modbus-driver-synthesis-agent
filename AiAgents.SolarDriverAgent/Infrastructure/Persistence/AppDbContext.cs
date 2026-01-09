using AiAgents.SolarDriverAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiAgents.SolarDriverAgent.Infrastructure.Persistence;

/// <summary>
/// Entity Framework DbContext za aplikaciju.
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<ProtocolTask> ProtocolTasks => Set<ProtocolTask>();
    public DbSet<DriverCode> DriverCodes => Set<DriverCode>();
    public DbSet<SimulationLog> SimulationLogs => Set<SimulationLog>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ProtocolTask konfiguracija
        modelBuilder.Entity<ProtocolTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PdfDocument).IsRequired();
            // SQLite koristi TEXT za dugačke stringove (bez max specifikacije)
            entity.Property(e => e.ExtractedSpecification);
            entity.Property(e => e.Status).IsRequired();

            entity.HasOne(e => e.CurrentDriver)
                  .WithOne()
                  .HasForeignKey<DriverCode>(d => d.ProtocolTaskId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.SimulationLogs)
                  .WithOne()
                  .HasForeignKey(l => l.ProtocolTaskId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        // DriverCode konfiguracija
        modelBuilder.Entity<DriverCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceCode).IsRequired();
            entity.Property(e => e.CodeHash).HasMaxLength(128);

            entity.HasIndex(e => e.ProtocolTaskId);
        });

        // SimulationLog konfiguracija
        modelBuilder.Entity<SimulationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.StackTrace);
            entity.Property(e => e.ExpectedValues).HasMaxLength(1000);
            entity.Property(e => e.ActualValues).HasMaxLength(1000);
            entity.Property(e => e.TestedRegisters).HasMaxLength(1000);

            entity.HasIndex(e => e.ProtocolTaskId);
            entity.HasIndex(e => e.ExecutedAt);
        });
    }
}
