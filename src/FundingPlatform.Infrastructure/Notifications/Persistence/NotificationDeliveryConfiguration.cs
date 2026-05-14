using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Notifications.Persistence;

/// <summary>
/// Spec 021 / T020 / NFR-005 — EF mapping for <see cref="NotificationDelivery"/>.
/// Schema lives in the dacpac (<c>dbo.NotificationDelivery.sql</c>) including
/// the filtered unique index. This config declares the index in EF model
/// metadata so EF treats UNIQUE-violation as the expected idempotency guard.
/// </summary>
public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDelivery", "dbo");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.OutboxId).IsRequired();

        builder.Property(e => e.EventType)
            .HasColumnType("varchar(64)")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.ApplicationId).IsRequired();
        builder.Property(e => e.VersionHistoryId).IsRequired();

        builder.Property(e => e.RecipientUserId)
            .HasMaxLength(450);

        builder.Property(e => e.RecipientEmail)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Provider)
            .HasColumnType("varchar(32)")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(e => e.ProviderMessageId)
            .HasMaxLength(256);

        builder.Property(e => e.Status)
            .HasColumnType("varchar(24)")
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(e => e.AttemptCount).HasDefaultValue(0);

        builder.Property(e => e.LastError)
            .HasMaxLength(2000);

        builder.Property(e => e.SentAt)
            .HasColumnType("datetime2(3)");

        // FR-020 — idempotency unique index, filtered on NON-NULL RecipientUserId
        // so synthetic rows can coexist. EF emits the filter expression so the
        // index matches the dacpac exactly.
        builder.HasIndex(e => new { e.EventType, e.ApplicationId, e.VersionHistoryId, e.RecipientUserId })
            .HasDatabaseName("UX_NotificationDelivery_DedupKey")
            .HasFilter("[RecipientUserId] IS NOT NULL")
            .IsUnique();

        builder.HasIndex(e => e.OutboxId).HasDatabaseName("IX_NotificationDelivery_OutboxId");
        builder.HasIndex(e => new { e.RecipientEmail, e.SentAt })
            .HasDatabaseName("IX_NotificationDelivery_RecipientEmail");
    }
}
