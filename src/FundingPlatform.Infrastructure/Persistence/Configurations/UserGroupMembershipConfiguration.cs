using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Spec 016 — maps <see cref="UserGroupMembership"/> to
/// <c>dbo.UserGroupMemberships</c>.</summary>
public class UserGroupMembershipConfiguration : IEntityTypeConfiguration<UserGroupMembership>
{
    public void Configure(EntityTypeBuilder<UserGroupMembership> builder)
    {
        builder.ToTable("UserGroupMemberships");
        builder.HasKey(m => new { m.UserId, m.GroupId });
        builder.Property(m => m.UserId).HasMaxLength(450).IsRequired();
        builder.Property(m => m.AssignedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Group)
            .WithMany(g => g.Memberships)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // FR-011..FR-014 — supports the reviewer-side group-overlap predicate.
        builder.HasIndex(m => new { m.GroupId, m.UserId })
            .HasDatabaseName("IX_UserGroupMemberships_GroupId_UserId");
    }
}
