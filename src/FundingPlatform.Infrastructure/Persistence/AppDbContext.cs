using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Notifications.Persistence;
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
    // Spec 035 — admin-configured per-category field set + per-item values.
    public DbSet<CategoryField> CategoryFields => Set<CategoryField>();
    public DbSet<CategoryFieldValue> CategoryFieldValues => Set<CategoryFieldValue>();
    public DbSet<ImpactTemplate> ImpactTemplates => Set<ImpactTemplate>();
    public DbSet<ImpactTemplateParameter> ImpactTemplateParameters => Set<ImpactTemplateParameter>();
    // Spec 035 (evolved 2026-06-16, D13/D14) — impact at the application level + per-item attribution.
    public DbSet<ApplicationImpact> ApplicationImpacts => Set<ApplicationImpact>();
    public DbSet<ItemImpact> ItemImpacts => Set<ItemImpact>();
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

    // Spec 020 — AI quote comparison cache + job queue.
    public DbSet<ComparisonArtifact> ComparisonArtifacts => Set<ComparisonArtifact>();
    public DbSet<ComparisonJob> ComparisonJobs => Set<ComparisonJob>();

    // Spec 021-email-notifications — transactional outbox + per-recipient delivery audit.
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    // Spec 029 — Fund (Fondo) aggregate above Process.
    public DbSet<Fund> Funds => Set<Fund>();

    // Spec 036 — funds-usage evidence files on AgreementExecuted applications.
    public DbSet<FundsUsageEvidence> FundsUsageEvidence => Set<FundsUsageEvidence>();

    // Spec 045 — financial disbursement core: disbursements, typed evidence, append-only ledger.
    public DbSet<Disbursement> Disbursements => Set<Disbursement>();
    public DbSet<DisbursementEvidence> DisbursementEvidence => Set<DisbursementEvidence>();
    public DbSet<DisbursementLedgerEntry> DisbursementLedgerEntries => Set<DisbursementLedgerEntry>();

    // Spec 046 — tranches (funding phases) + per-line payment attribution join.
    public DbSet<Tranche> Tranches => Set<Tranche>();
    public DbSet<DisbursementLineAllocation> DisbursementLineAllocations => Set<DisbursementLineAllocation>();

    // Spec 047 — evidence graph (versioned nodes + M:N line allocation).
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<EvidenceVersion> EvidenceVersions => Set<EvidenceVersion>();
    public DbSet<EvidenceLineAllocation> EvidenceLineAllocations => Set<EvidenceLineAllocation>();

    // Spec 037 — admin-managed companies (Empresas) owned by applicants.
    public DbSet<Company> Companies => Set<Company>();

    // Spec 040 — per-stage checklist templates + recorded application responses.
    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();
    public DbSet<ChecklistTemplateItem> ChecklistTemplateItems => Set<ChecklistTemplateItem>();
    public DbSet<ApplicationChecklistResponse> ApplicationChecklistResponses => Set<ApplicationChecklistResponse>();

    // Spec 021-feedback-session-may13 — Process / Plantilla aggregates,
    // Province/Cantón catalog, PasswordResetToken single-use marker.
    public DbSet<Process> Processes => Set<Process>();
    // Spec 044 — general per-Process calendar items (reception windows gate submission).
    public DbSet<ProcessEvent> ProcessEvents => Set<ProcessEvent>();
    public DbSet<Plantilla> Plantillas => Set<Plantilla>();
    public DbSet<ProcessPlantilla> ProcessPlantillas => Set<ProcessPlantilla>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<Canton> Cantons => Set<Canton>();
    // Spec 025 — distrito catalog (third tier of the supplier-branch location cascade).
    public DbSet<District> Districts => Set<District>();
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
