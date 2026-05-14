using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Notifications.Persistence;

/// <summary>
/// Spec 021 / T019 / NFR-005 — EF mapping for <see cref="NotificationOutbox"/>.
/// Schema is owned by the dacpac (<c>dbo.NotificationOutbox.sql</c>); this
/// only wires column types + the RowVersion concurrency token. No EF
/// migrations are produced.
/// </summary>
public class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> builder)
    {
        builder.ToTable("NotificationOutbox", "dbo");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.EventType)
            .HasColumnType("varchar(64)")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.ApplicationId).IsRequired();
        builder.Property(e => e.VersionHistoryId).IsRequired();

        builder.Property(e => e.PayloadJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.Status)
            .HasColumnType("varchar(16)")
            .HasMaxLength(16)
            .HasDefaultValue("Pending")
            .IsRequired();

        builder.Property(e => e.AttemptCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.LastError)
            .HasMaxLength(2000);

        builder.Property(e => e.NextAttemptAt)
            .HasColumnType("datetime2(3)");

        // FR-004 — optimistic concurrency on worker claim.
        builder.Property(e => e.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.HasIndex(e => new { e.Status, e.NextAttemptAt })
            .HasDatabaseName("IX_NotificationOutbox_Status_NextAttemptAt");

        builder.HasIndex(e => new { e.ApplicationId, e.CreatedAt })
            .HasDatabaseName("IX_NotificationOutbox_ApplicationId");

        // Spec 021 — dacpac owns the FK_NotificationOutbox_VersionHistory + FK_NotificationOutbox_Applications
        // constraints; EF only reads the table, no navigation needed in this v1.
        // The VersionHistory CLR-side navigation is ignored so EF doesn't try to
        // discover an implicit relationship.
        builder.Ignore(e => e.VersionHistory);
    }
}
