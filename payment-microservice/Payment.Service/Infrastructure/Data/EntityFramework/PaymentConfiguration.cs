using ECommerce.Shared.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Service.Domain;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentConfiguration : IEntityTypeConfiguration<Domain.Payment>
{
    private static readonly DateTime SeedEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Domain.Payment> builder)
    {
        builder.HasKey(p => p.PaymentId);

        builder.Property(p => p.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .HasConversion<int>();

        builder.Property(p => p.ProviderReference)
            .HasMaxLength(200);

        builder.HasIndex(p => p.OrderId)
            .IsUnique();

        builder.HasData(
            new
            {
                PaymentId = QaPersonas.PaymentAuthorizedId,
                OrderId = QaPersonas.OrderAuthorizedId,
                CustomerId = QaPersonas.CustomerHappyId.ToString(),
                Amount = QaPersonas.PaymentHappyAmount,
                Currency = "USD",
                Status = PaymentStatus.Authorized,
                ProviderReference = QaPersonas.PaymentAuthorizedProviderRef,
                CreatedAt = SeedEpoch,
                UpdatedAt = SeedEpoch,
            },
            new
            {
                PaymentId = QaPersonas.PaymentCapturedId,
                OrderId = QaPersonas.OrderCapturedId,
                CustomerId = QaPersonas.CustomerHappyId.ToString(),
                Amount = QaPersonas.PaymentHappyAmount,
                Currency = "USD",
                Status = PaymentStatus.Captured,
                ProviderReference = QaPersonas.PaymentCapturedProviderRef,
                CreatedAt = SeedEpoch,
                UpdatedAt = SeedEpoch,
            });
    }
}
