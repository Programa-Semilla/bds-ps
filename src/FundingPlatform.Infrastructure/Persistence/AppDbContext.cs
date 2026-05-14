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
    // Spec 021 / FR-005 / NFR-001 — legacy `dbo.Impacts` table dropped in
    // Phase 2a; the Domain.Entities.Impact class remains as dead code referenced
    // only by historical Application/Web call sites that will be rewritten in
    // later phases. The class is NOT mapped — no DbSet, no IEntityTypeConfiguration.
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

    // Spec 015 — multi-currency catalog + reference rates.
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // Spec 016 — group catalog, user-group memberships, admin audit log.
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<UserGroupMembership> UserGroupMemberships => Set<UserGroupMembership>();
    public DbSet<AdminAuditEvent> AdminAuditEvents => Set<AdminAuditEvent>();

    // Spec 021 — Process / Plantilla aggregates, Province/Cantón catalog,
    // PasswordResetToken single-use marker.
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<Plantilla> Plantillas => Set<Plantilla>();
    public DbSet<ProcessPlantilla> ProcessPlantillas => Set<ProcessPlantilla>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<Canton> Cantons => Set<Canton>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

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
