using FundingPlatform.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<AppEntity> Applications => Set<AppEntity>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ImpactTemplate> ImpactTemplates => Set<ImpactTemplate>();
    public DbSet<ImpactTemplateParameter> ImpactTemplateParameters => Set<ImpactTemplateParameter>();
    public DbSet<Impact> Impacts => Set<Impact>();
    public DbSet<ImpactParameterValue> ImpactParameterValues => Set<ImpactParameterValue>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierBranch> SupplierBranches => Set<SupplierBranch>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<VersionHistory> VersionHistories => Set<VersionHistory>();
    public DbSet<ApplicantResponse> ApplicantResponses => Set<ApplicantResponse>();
    public DbSet<Appeal> Appeals => Set<Appeal>();
    public DbSet<FundingAgreement> FundingAgreements => Set<FundingAgreement>();
    public DbSet<SignedUpload> SignedUploads => Set<SignedUpload>();
    public DbSet<SigningReviewDecision> SigningReviewDecisions => Set<SigningReviewDecision>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Hide the system sentinel admin from every default user enumeration.
        // Bypassed by SentinelAwareUserStore (sign-in path) and by service-layer
        // guard fetches that explicitly call IgnoreQueryFilters().
        builder.Entity<ApplicationUser>().HasQueryFilter(u => !u.IsSystemSentinel);

        // Bind Application.FundingAgreement to its private backing field.
        // Done after ApplyConfigurationsFromAssembly so the navigation metadata exists.
        builder.Entity<AppEntity>()
            .Navigation(a => a.FundingAgreement)
            .HasField("_fundingAgreement")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
