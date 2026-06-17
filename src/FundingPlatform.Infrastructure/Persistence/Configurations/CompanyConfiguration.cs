// Spec 037 — see specs/037-applicant-companies/data-model.md (Company aggregate).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 037 / FR-001 — maps <see cref="Company"/> to <c>dbo.Companies</c>. Mirrors
/// <c>FundConfiguration</c>. Per-applicant active-name uniqueness is enforced by the
/// dacpac filtered unique index <c>UX_Companies_ApplicantId_Name</c> (mirrored here).
/// </summary>
public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.ApplicantId).IsRequired();

        builder.Property(c => c.Name)
            .HasMaxLength(Company.MaxNameLength)
            .IsRequired();

        builder.Property(c => c.ArchivedAt);

        builder.Property(c => c.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        builder.Property(c => c.UpdatedAt).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.ApplicantId)
            .HasDatabaseName("IX_Companies_ApplicantId");

        builder.HasIndex(c => new { c.ApplicantId, c.Name })
            .IsUnique()
            .HasFilter("[ArchivedAt] IS NULL")
            .HasDatabaseName("UX_Companies_ApplicantId_Name");
    }
}
