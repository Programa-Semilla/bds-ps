using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class ApplicationConfiguration : IEntityTypeConfiguration<AppEntity>
{
    public void Configure(EntityTypeBuilder<AppEntity> builder)
    {
        builder.ToTable("Applications");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ApplicantId).IsRequired();
        builder.HasIndex(a => a.ApplicantId).HasDatabaseName("IX_Applications_ApplicantId");

        // Spec 029 / FR-017 — authoritative Group anchor (→ Process → Fund).
        // Required; reviewer visibility is unchanged (the anchor is additive).
        builder.Property(a => a.GroupId).IsRequired();
        builder.HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(a => a.GroupId).HasDatabaseName("IX_Applications_GroupId");

        // Spec 018 / FR-015 / FR-016 — required commercial entity name, ≤200 chars.
        builder.Property(a => a.CompanyName).IsRequired().HasMaxLength(200);

        builder.Property(a => a.State).IsRequired();
        builder.HasIndex(a => a.State).HasDatabaseName("IX_Applications_State");

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();
        builder.Property(a => a.SubmittedAt);

        builder.Property(a => a.RowVersion).IsRowVersion();

        // Spec 021 / FR-008 — PublicCode value object stored as a CHAR(9) string.
        // Nullable in-domain (pre-AssignPublicCode) but NOT NULL at the DB level;
        // the Application-layer save path is responsible for stamping the code
        // via IPublicCodeGenerator before the first SaveChanges.
        builder.Property(a => a.PublicCode)
            .HasConversion(
                vo => vo == null ? null : vo.Value,
                str => str == null ? null : new PublicCode(str))
            .HasColumnName("PublicCode")
            .HasColumnType("CHAR(9)")
            .IsRequired();
        builder.HasIndex(a => a.PublicCode)
            .IsUnique()
            .HasDatabaseName("UX_Applications_PublicCode");

        // Spec 021 / FR-005 — per-Application ImpactTemplate selection.
        builder.Property(a => a.ImpactTemplateId);
        builder.HasOne(a => a.ImpactTemplate)
            .WithMany()
            .HasForeignKey(a => a.ImpactTemplateId)
            .OnDelete(DeleteBehavior.NoAction);

        // Spec 021 / FR-005 — re-parented parameter values. The Domain entity
        // still carries a vestigial `ImpactId` from the legacy schema; we
        // ignore it and use a shadow `ApplicationId` FK (column already exists
        // per dbo.ImpactParameterValues.sql).
        builder.HasMany(a => a.ImpactParameterValues)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(AppEntity.ImpactParameterValues))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Spec 021 / R-2 — at-most-once delivery bitmask for stage-expiry reminders.
        builder.Property(a => a.RemindersSentMask).IsRequired().HasDefaultValue((byte)0);

        // Spec 021 / FR-006 — stage-entry timestamp; defaulted server-side so a
        // historical row imported without an explicit value still passes guards.
        builder.Property(a => a.StageEnteredAt).IsRequired();

        // Spec 021 / FR-021 — soft-delete column. R-10 — dashboard read paths
        // filter on this via IApplicationQueryFilter.ExcludeDeleted; the
        // column is left NULL for live rows.
        builder.Property(a => a.DeletedAt);
        builder.HasIndex(a => a.DeletedAt).HasDatabaseName("IX_Applications_DeletedAt");

        builder.HasMany(a => a.Items)
            .WithOne()
            .HasForeignKey(i => i.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.VersionHistory)
            .WithOne(v => v.Application)
            .HasForeignKey(v => v.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        var applicantResponsesNav = builder.Metadata.FindNavigation(nameof(AppEntity.ApplicantResponses))!;
        applicantResponsesNav.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.ApplicantResponses)
            .WithOne()
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        var appealsNav = builder.Metadata.FindNavigation(nameof(AppEntity.Appeals))!;
        appealsNav.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.Appeals)
            .WithOne()
            .HasForeignKey(ap => ap.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
