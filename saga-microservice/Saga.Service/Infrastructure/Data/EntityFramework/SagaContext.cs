using Microsoft.EntityFrameworkCore;
using Saga.Service.Domain;
using Saga.Service.Domain.OrderSaga;
using Saga.Service.Domain.RefundSaga;

namespace Saga.Service.Infrastructure.Data.EntityFramework;

internal class SagaContext : DbContext
{
    public SagaContext(DbContextOptions<SagaContext> options)
        : base(options)
    {
    }

    public DbSet<SagaInstance> SagaInstances { get; set; } = null!;

    public DbSet<OrderSagaState> OrderSagaStates { get; set; } = null!;

    public DbSet<RefundSagaState> RefundSagaStates { get; set; } = null!;

    public DbSet<SagaTransition> SagaTransitions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SagaInstance>(builder =>
        {
            builder.HasKey(s => s.SagaId);

            builder.Property(s => s.SagaType)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(s => s.CurrentStep)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(s => s.Version)
                .IsRowVersion();

            builder.HasIndex(s => new { s.SagaType, s.Status, s.NextTimeoutAt });
        });

        modelBuilder.Entity<OrderSagaState>(builder =>
        {
            builder.HasKey(s => s.SagaId);

            builder.Property(s => s.LastStepResult)
                .HasMaxLength(128);

            builder.Property(s => s.CompensationOrigin)
                .HasMaxLength(64);

            builder.Property(s => s.Amount)
                .HasPrecision(18, 2);

            builder.HasIndex(s => s.OrderId)
                .IsUnique();

            builder.HasOne(s => s.SagaInstance)
                .WithOne(s => s.OrderSagaState)
                .HasForeignKey<OrderSagaState>(s => s.SagaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefundSagaState>(builder =>
        {
            builder.HasKey(s => s.SagaId);

            builder.Property(s => s.Currency)
                .HasMaxLength(3)
                .IsRequired();

            builder.Property(s => s.LastStepResult)
                .HasMaxLength(128);

            builder.Property(s => s.RefundAmount)
                .HasPrecision(18, 2);

            builder.HasIndex(s => s.OrderId)
                .IsUnique();

            builder.HasOne(s => s.SagaInstance)
                .WithOne(s => s.RefundSagaState)
                .HasForeignKey<RefundSagaState>(s => s.SagaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SagaTransition>(builder =>
        {
            builder.HasKey(t => t.Id);

            builder.Property(t => t.FromStep)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(t => t.ToStep)
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(t => t.TriggerKind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(t => t.Error)
                .HasMaxLength(1024);

            builder.HasOne(t => t.SagaInstance)
                .WithMany(s => s.Transitions)
                .HasForeignKey(t => t.SagaId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
