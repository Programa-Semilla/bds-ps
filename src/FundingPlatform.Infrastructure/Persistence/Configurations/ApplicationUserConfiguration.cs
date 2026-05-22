using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 016 — wires the <see cref="ApplicationUser.Memberships"/> navigation.
/// The Identity-side mapping (table name, PK, columns) comes from
/// <c>IdentityDbContext</c>'s defaults; this configuration only adds the
/// reverse end of the membership relationship.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasMany(u => u.Memberships)
            .WithOne(m => m.User)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Spec 021 / FR-018 / FR-019 — admin-set free-text personal code,
        // read-only to the user on /profile, visible on admin reports.
        builder.Property(u => u.CodigoPersonal).HasMaxLength(40);

        builder.Metadata
            .FindNavigation(nameof(ApplicationUser.Memberships))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
