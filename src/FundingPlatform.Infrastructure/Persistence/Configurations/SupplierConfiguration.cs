using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.LegalId).IsRequired().HasMaxLength(50);
        builder.HasIndex(s => s.LegalId).IsUnique().HasDatabaseName("UX_Suppliers_LegalId");

        // Spec 026 — nullable byte-enum stored as TINYINT.
        builder.Property(s => s.IdentificationType).HasConversion<byte?>();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(300);

        // Spec 038 — enumerated regulatory statuses + per-field reviewed metadata.
        // Nullable byte-enums stored as TINYINT (the IdentificationType pattern).
        builder.Property(s => s.HaciendaStatus).HasConversion<byte?>();
        builder.Property(s => s.HaciendaLastReviewedAt);
        builder.Property(s => s.HaciendaLastReviewedBy).HasMaxLength(450);
        builder.Property(s => s.HaciendaLastReviewedSource).HasConversion<byte?>();

        builder.Property(s => s.CcssStatus).HasConversion<byte?>();
        builder.Property(s => s.CcssLastReviewedAt);
        builder.Property(s => s.CcssLastReviewedBy).HasMaxLength(450);
        builder.Property(s => s.CcssLastReviewedSource).HasConversion<byte?>();

        builder.Property(s => s.SicopStatus).HasConversion<byte?>();
        builder.Property(s => s.SicopLastReviewedAt);
        builder.Property(s => s.SicopLastReviewedBy).HasMaxLength(450);
        builder.Property(s => s.SicopLastReviewedSource).HasConversion<byte?>();

        builder.Property(s => s.IsPmeOrPyme).IsRequired();
        builder.Property(s => s.HasWarning).IsRequired();
        builder.Property(s => s.WarningNote).HasMaxLength(1000);

        // Spec 043 — per-provider Hacienda sync outcome. TINYINT-enum needs explicit
        // HasConversion<byte?>() or real-SQL materialization throws Byte→Int32 (spec 040 lesson).
        builder.Property(s => s.HaciendaSyncAttemptAt);
        builder.Property(s => s.HaciendaSyncOutcome).HasConversion<byte?>();
        builder.Property(s => s.HaciendaSyncError).HasMaxLength(500);

        // Spec 038 / D15 — optimistic concurrency token.
        builder.Property(s => s.RowVersion).IsRowVersion();

        // Spec 013 lifecycle.
        builder.Property(s => s.VerificationStatus)
               .HasConversion<byte>()
               .IsRequired();
        builder.Property(s => s.CreatedByApplicantId);
        builder.Property(s => s.VerifiedByUserId).HasMaxLength(450);
        builder.Property(s => s.VerifiedAt);
        builder.Property(s => s.RejectionReason).HasMaxLength(1000);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // 1:N branches (aggregate boundary). Backing field gives the entity sole
        // write authority via Supplier.AddBranch / EditBranch.
        builder.HasMany(s => s.Branches)
               .WithOne()
               .HasForeignKey(b => b.SupplierId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Branches)
               .HasField("_branches")
               .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
