using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Payment.Service.Infrastructure.Data.EntityFramework;

internal class PaymentConfiguration : IEntityTypeConfiguration<Models.Payment>
{
    public void Configure(EntityTypeBuilder<Models.Payment> builder)
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
    }
}
