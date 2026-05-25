using ECommerce.Shared.Infrastructure.DeadLetter.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerce.Shared.Infrastructure.DeadLetter;

internal sealed class DeadLetterMessageConfiguration : IEntityTypeConfiguration<DeadLetterMessage>
{
    public void Configure(EntityTypeBuilder<DeadLetterMessage> builder)
    {
        builder.ToTable("dead_letter_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(512).IsRequired();
        builder.Property(x => x.RoutingKey).HasColumnName("routing_key").HasMaxLength(512).IsRequired();
        builder.Property(x => x.OriginalQueue).HasColumnName("original_queue").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Service).HasColumnName("service").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(1024).IsRequired();
        builder.Property(x => x.StackTrace).HasColumnName("stack_trace");
        builder.Property(x => x.Attempts).HasColumnName("attempts");
        builder.Property(x => x.FailedAt).HasColumnName("failed_at");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.ReplayedAt).HasColumnName("replayed_at");
        builder.Property(x => x.ReplayedBy).HasColumnName("replayed_by").HasMaxLength(256);
        builder.Property(x => x.DiscardedAt).HasColumnName("discarded_at");
        builder.Property(x => x.DiscardedBy).HasColumnName("discarded_by").HasMaxLength(256);
        builder.Property(x => x.DiscardReason).HasColumnName("discard_reason").HasMaxLength(1024);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.Origin).HasColumnName("origin").HasConversion<int>().HasDefaultValue(Models.DeadLetterOrigin.DeadLetter);

        builder.HasIndex(x => x.FailedAt);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Origin);
    }
}
