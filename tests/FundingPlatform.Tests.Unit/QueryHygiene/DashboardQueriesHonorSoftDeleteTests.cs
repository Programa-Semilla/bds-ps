// Spec 021 — see specs/021-feedback-session-may13/tasks.md T150 and
// research.md R-10 + FR-021 + SC-011.

using System.Text.RegularExpressions;

namespace FundingPlatform.Tests.Unit.QueryHygiene;

/// <summary>
/// Spec 021 / US8 / T150 / FR-021 / R-10 / SC-011 — structural audit that
/// pins every dashboard / projection / read-path source file under
/// <c>src/FundingPlatform.Application/</c> and
/// <c>src/FundingPlatform.Infrastructure/</c> + the controllers under
/// <c>src/FundingPlatform.Web/Controllers/</c> to route Applications reads
/// through <see cref="FundingPlatform.Application.Abstractions.IApplicationQueryFilter.ExcludeDeleted"/>.
///
/// <para>Approach: pragmatic string-scan of checked-in .cs files (Roslyn
/// would be more precise but adds churn; reflection over compiled assemblies
/// would not see member references inside method bodies reliably). The scan
/// flags any unguarded <c>X.Applications.</c> token (typically
/// <c>_db.Applications</c>, <c>_context.Applications</c>, <c>db.Applications</c>,
/// <c>_dbContext.Applications</c>) and lets a small exemption table opt
/// individual call sites out, each documented inline with a Spec-021 anchor.</para>
///
/// <para>The structural test is intentionally pessimistic: a NEW Applications
/// read that fails to route through <c>ExcludeDeleted</c> will fail this
/// test unless the author either (a) wraps it through the filter or (b) adds
/// the file to the exemption list with a written rationale. Exemptions are
/// scoped to FILES, not lines — false positives in a file are documented in
/// the table comment.</para>
/// </summary>
[TestFixture]
public class DashboardQueriesHonorSoftDeleteTests
{
    // The string-scan matches EF DbSet access on AppDbContext, anchoring on
    // the standard variable names used in this codebase (`_db`, `_dbContext`,
    // `_context`, `db`, `dbContext`, `context`). Anchoring keeps the
    // FundingPlatform.Application.Applications.* namespace + the
    // FundingPlatform.Web.Controllers.Applications.* references from
    // triggering false positives — we only flag actual DbSet usage.
    private static readonly Regex ApplicationsTokenRegex = new(
        @"\b(?<owner>_db|_dbContext|_context|db|dbContext|context)\.Applications\.[A-Za-z]",
        RegexOptions.Compiled);

    /// <summary>
    /// Files whose <c>.Applications.</c> matches are NOT dashboard read paths.
    /// Each entry MUST cite the Spec-021 anchor justifying the exemption.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exemptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Repository write-side helpers (.AddAsync / .Update) on the
            // ApplicationRepository — not dashboard reads. The by-Id read
            // helpers (GetByIdAsync / GetByIdWithDetailsAsync /
            // GetByIdWithResponseAndAppealsAsync) intentionally do NOT filter
            // soft-delete: admin write / detail paths must still load deleted
            // rows. See ApplicationRepository.cs inline comments for the
            // per-method rationale (Spec 021 / T152 / R-10).
            ["src/FundingPlatform.Infrastructure/Persistence/Repositories/ApplicationRepository.cs"] =
                "T152 — file-level exemption; per-method comments justify the by-Id reads and write helpers.",

            // PublicCode collision check — uniqueness probe, not a dashboard
            // read. Soft-deleted rows STILL hold their PublicCode (we do not
            // re-issue codes), so the collision predicate must scan the full
            // table including soft-deleted rows (Spec 021 / FR-008).
            ["src/FundingPlatform.Infrastructure/PublicCodes/PublicCodeGenerator.cs"] =
                "T152 — uniqueness probe; deleted rows still own their PublicCode (FR-008).",

            // Autosave handler — single-row by-PublicCode write path scoped to
            // the owning applicant. The applicant cannot autosave a deleted
            // draft (the dashboard would no longer expose the row), but the
            // handler itself does not need to re-filter; a deleted-but-known
            // PublicCode would fail the applicant-ownership check downstream
            // (Spec 021 / FR-021 / T152 — write-side path, not a dashboard).
            ["src/FundingPlatform.Infrastructure/Services/AutosaveFieldHandler.cs"] =
                "T152 — write-side single-row lookup, not a dashboard surface.",

            // SubmitApplicationHandler — single-row by-Id write. Not a
            // dashboard surface (Spec 021 / T152 / FR-017).
            ["src/FundingPlatform.Infrastructure/Services/SubmitApplicationHandler.cs"] =
                "T152 — write-side single-row submit handler, not a dashboard surface.",

            // SupplierRepository — supplier-listing predicate that joins on
            // Applications to scope to a Process. A soft-deleted Application
            // is rare here (suppliers outlive their owning applications) and
            // filtering would suppress otherwise-valid suppliers; the read is
            // a supplier-discovery surface, not an applicants/admin/reviewer
            // dashboard. Documented exemption (Spec 021 / T152 / R-10).
            ["src/FundingPlatform.Infrastructure/Persistence/Repositories/SupplierRepository.cs"] =
                "T152 — supplier-listing join; not a dashboard surface (suppliers outlive Apps).",

            // Web controller — every `.Applications.` match is a single-row
            // by-Id or by-PublicCode lookup used to populate banners / submit
            // by PublicCode. Not list-style dashboard reads; the dashboard
            // surfaces these controllers serve are sourced from the projection
            // helpers (which DO route through ExcludeDeleted). See
            // ApplicationController.PopulateStageBannerAsync / SubmitByPublicCode.
            ["src/FundingPlatform.Web/Controllers/ApplicationController.cs"] =
                "T152 — single-row by-Id / by-PublicCode reads; not dashboard lists.",

            // Review controller — single-row banner-builder lookup
            // (BuildStageBannersAsync). The reviewer queue / signing inbox
            // lists themselves are sourced from projection helpers that DO
            // filter (ApplicationRepository.GetByStateForReviewerAsync +
            // SignedUploadRepository.GetPendingInboxAsync).
            ["src/FundingPlatform.Web/Controllers/ReviewController.cs"] =
                "T152 — banner builder for already-listed apps; not a list source.",

            // AccountController — dev-only helpers (BackdateStageEntered,
            // SoftDeleteApplication) operate on specific Ids by design;
            // production environments 404 the routes (Spec 021 / T154).
            ["src/FundingPlatform.Web/Controllers/AccountController.cs"] =
                "T152/T154 — dev-only single-row admin helpers.",

            // NotificationOutboxWriter (spec 021-email-notifications) —
            // workflow-event helper: resolves the applicant's group
            // memberships at Submit/SendBack/Approve/Reject time so the
            // recipient resolver can fan out reviewer-bucket emails. Not a
            // dashboard surface; the Application aggregate is by-Id, single
            // row. Soft-delete races are tolerated by design — workflow
            // events committed before delete already reflect the historical
            // truth, and the outbox worker is idempotent. (Spec 021-email
            // FR-001 / 021-feedback-session-may13 R-10 cross-ref.)
            ["src/FundingPlatform.Infrastructure/Notifications/Persistence/NotificationOutboxWriter.cs"] =
                "Spec 021-email-notifications — workflow-event read, not dashboard surface.",
        };

    /// <summary>
    /// Resolves the repository root by walking up from the test assembly
    /// location until a directory containing <c>FundingPlatform.slnx</c> is
    /// found. Works under both <c>dotnet test</c> and IDE runners.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FundingPlatform.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate FundingPlatform.slnx by walking up from " +
            AppContext.BaseDirectory);
    }

    private static IEnumerable<string> EnumerateScannedFiles(string repoRoot)
    {
        var roots = new[]
        {
            Path.Combine(repoRoot, "src", "FundingPlatform.Application"),
            Path.Combine(repoRoot, "src", "FundingPlatform.Infrastructure"),
            Path.Combine(repoRoot, "src", "FundingPlatform.Web", "Controllers"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                // Skip auto-generated build artefacts that occasionally land in
                // bin/obj (defensive — they're already outside `src/`).
                var rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                yield return path;
            }
        }
    }

    private static bool IsExempt(string relativePath)
    {
        // Normalise on `/` so the table works on Windows + Linux.
        var key = relativePath.Replace('\\', '/');
        return Exemptions.ContainsKey(key);
    }

    [Test]
    public void EveryApplicationsRead_RoutesThroughExcludeDeleted_OrIsAnExemptedCallSite()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var path in EnumerateScannedFiles(repoRoot))
        {
            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (IsExempt(relative)) continue;

            var text = File.ReadAllText(path);

            // First gate: any `.Applications.` reference at all? If not, the
            // file is uninteresting (LINQ projections + cross-aggregate joins
            // do not show up here).
            if (!ApplicationsTokenRegex.IsMatch(text)) continue;

            // The file references `.Applications.`. The guard: every match
            // MUST be either (a) routed through `ExcludeDeleted(` somewhere
            // in the file, OR (b) be a write-side call (Add/Update/AddAsync).
            // We require the helper-call token to appear at file level — the
            // exemption table covers the few legitimate read-without-filter
            // call sites (uniqueness probes, by-Id loads, write paths).
            //
            // Inline string literals like `"dbo.Applications"` would have
            // been excluded by the regex's required follow-up `[A-Za-z]`
            // (matches `.X` where X starts with a letter); the SQL literal
            // ends in `s"` not `s.X`, so it does not match.
            if (text.Contains("ExcludeDeleted(", StringComparison.Ordinal)) continue;

            // Allow files that ONLY reference `.Applications.` for write-side
            // operations. These compile-time invariants are: `.AddAsync(`,
            // `.Add(`, `.Update(`, `.Remove(`. If every match is a write,
            // the file is fine. We materialise the unique follow-up methods
            // and check the set.
            var followups = ApplicationsTokenRegex.Matches(text)
                .Select(m =>
                {
                    var start = m.Index + m.Length - 1; // last captured char
                    // Walk forward from the matched starting letter to grab
                    // the rest of the identifier.
                    var end = start;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                    {
                        end++;
                    }
                    return text.Substring(start, end - start);
                })
                .ToHashSet(StringComparer.Ordinal);

            var writeOnlyTokens = new HashSet<string>(StringComparer.Ordinal)
            {
                "AddAsync", "Add", "Update", "Remove", "RemoveRange",
            };
            if (followups.All(f => writeOnlyTokens.Contains(f))) continue;

            offenders.Add(relative);
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Spec 021 / FR-021 / SC-011 — every dashboard / projection read on " +
            "`AppDbContext.Applications` MUST route through " +
            "`IApplicationQueryFilter.ExcludeDeleted` (see research.md R-10). " +
            "Files violating this contract: " +
            (offenders.Count == 0 ? "(none)" : string.Join(", ", offenders)) +
            ". If a new file legitimately reads Applications without the filter " +
            "(uniqueness probe, by-Id write path), add it to the `Exemptions` " +
            "table in DashboardQueriesHonorSoftDeleteTests with a Spec-021 anchor.");
    }

    [Test]
    public void IApplicationQueryFilter_ExcludeDeleted_ExistsAndFiltersOnDeletedAtIsNull()
    {
        var repoRoot = FindRepoRoot();
        var implPath = Path.Combine(repoRoot,
            "src", "FundingPlatform.Infrastructure", "Persistence", "ApplicationQueryFilter.cs");
        Assert.That(File.Exists(implPath), Is.True,
            "Spec 021 / R-10 — ApplicationQueryFilter.cs MUST exist (centralised soft-delete predicate).");

        var text = File.ReadAllText(implPath);
        Assert.That(text.Contains("a.DeletedAt == null", StringComparison.Ordinal), Is.True,
            "Spec 021 / R-10 — ExcludeDeleted MUST predicate on `DeletedAt == null`.");
    }
}
