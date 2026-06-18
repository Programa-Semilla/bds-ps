using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 040 / D6 — maps <see cref="ApplicationChecklistResponse"/> to
/// <c>dbo.ApplicationChecklistResponses</c>. Both FKs are <c>Restrict</c> (NO ACTION):
/// applications are soft-deleted and template items are deactivated, never hard-deleted,
/// so historical responses survive and no multiple-cascade-path conflict arises.
/// </summary>
public class ApplicationChecklistResponseConfiguration
    : IEntityTypeConfiguration<ApplicationChecklistResponse>
{
    public void Configure(EntityTypeBuilder<ApplicationChecklistResponse> builder)
    {
        builder.ToTable("ApplicationChecklistResponses");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ApplicationId).IsRequired();
        builder.Property(r => r.Stage).IsRequired();
        builder.Property(r => r.ChecklistTemplateItemId).IsRequired();
        builder.Property(r => r.ItemTextSnapshot).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.NonComplianceReason).HasMaxLength(1000);
        builder.Property(r => r.CompletedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(r => r.CompletedAtUtc).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasIndex(r => new { r.ApplicationId, r.Stage })
            .HasDatabaseName("IX_ApplicationChecklistResponses_ApplicationId_Stage");

        builder.HasOne<AppEntity>()
            .WithMany()
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ChecklistTemplateItem>()
            .WithMany()
            .HasForeignKey(r => r.ChecklistTemplateItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
