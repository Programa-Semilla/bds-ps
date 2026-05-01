using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.StorageMigration;

/// <summary>
/// Spec 014 / US4 / T040–T043 — one-shot, idempotent migration tool that walks a
/// legacy on-disk root, looks up each file's owning DB row to derive its
/// canonical <see cref="ObjectKey"/>, and re-uploads through
/// <see cref="IObjectStorage"/> while emitting a JSON Lines manifest.
///
/// Subcommands:
///   migrate   (default)  walk + upload + manifest (FR-024)
///   verify                re-read a manifest and assert every Uploaded entry still exists (T042)
///
/// Exit codes:
///   0  every entry uploaded or skipped (no Failed)
///   1  one or more entries failed
///   2  bad command-line arguments
///   3  configuration / DI failure
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var parsed = MigrationCliOptions.Parse(args);

            if (parsed.ShowHelp)
            {
                Console.Out.WriteLine(MigrationCliOptions.HelpText);
                return 0;
            }

            using var host = BuildHost(parsed);
            await host.StartAsync().ConfigureAwait(false);
            try
            {
                return parsed.Subcommand switch
                {
                    MigrationSubcommand.Migrate => await RunMigrateAsync(host.Services, parsed, CancellationToken.None).ConfigureAwait(false),
                    MigrationSubcommand.Verify => await RunVerifyAsync(host.Services, parsed, CancellationToken.None).ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"Unknown subcommand {parsed.Subcommand}."),
                };
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }
        catch (CliArgumentException ex)
        {
            await Console.Error.WriteLineAsync($"argument error: {ex.Message}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(MigrationCliOptions.HelpText).ConfigureAwait(false);
            return 2;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"migration tool failed: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            return 3;
        }
    }

    private static IHost BuildHost(MigrationCliOptions parsed)
    {
        var builder = Host.CreateApplicationBuilder();

        // Wire the same Storage:* config the Web project uses, derived from the CLI flags.
        var inMemory = new Dictionary<string, string?>
        {
            ["Storage:Provider"] = parsed.Provider,
        };
        if (!string.IsNullOrWhiteSpace(parsed.ConnectionString))
            inMemory["Storage:ConnectionString"] = parsed.ConnectionString;
        if (!string.IsNullOrWhiteSpace(parsed.AccountReference))
            inMemory["Storage:AccountReference"] = parsed.AccountReference;
        if (!string.IsNullOrWhiteSpace(parsed.LocalRoot))
            inMemory["Storage:LocalFilesystem:RootPath"] = parsed.LocalRoot;

        builder.Configuration.AddInMemoryCollection(inMemory);

        builder.Services.AddObjectStorage(builder.Configuration);

        // Register DbContext. Connection string can come from --db-connection-string,
        // env var DOTNET_CONNECTIONSTRINGS__DEFAULTCONNECTION, or the DefaultConnection
        // section. Tools don't auto-resolve Aspire references; surface a clear error.
        var connectionString = parsed.DbConnectionString
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        builder.Services.AddDbContext<AppDbContext>(opts =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "No SQL connection string provided. Pass --db-connection-string <conn> or set ConnectionStrings__DefaultConnection.");
            opts.UseSqlServer(connectionString);
        });

        builder.Logging.AddSimpleConsole(opts =>
        {
            opts.SingleLine = true;
            opts.IncludeScopes = false;
        });

        return builder.Build();
    }

    private static async Task<int> RunMigrateAsync(
        IServiceProvider services,
        MigrationCliOptions opts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.LegacyRoot))
            throw new CliArgumentException("--legacy-root is required for the migrate subcommand.");
        if (!Directory.Exists(opts.LegacyRoot))
            throw new CliArgumentException($"--legacy-root directory does not exist: {opts.LegacyRoot}");
        if (string.IsNullOrWhiteSpace(opts.ManifestOut))
            throw new CliArgumentException("--manifest-out is required for the migrate subcommand.");

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var storage = sp.GetRequiredService<IObjectStorage>();
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("StorageMigration");

        var resolver = new LegacyRowResolver(db);
        await resolver.LoadAsync(ct).ConfigureAwait(false);

        var runner = new MigrationRunner(storage, resolver, logger);
        var summary = await runner.RunAsync(opts.LegacyRoot!, opts.ManifestOut!, opts.Parallelism, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Migration finished: {Total} files. Uploaded={Uploaded}, Skipped={Skipped}, Failed={Failed}. Manifest: {Manifest}",
            summary.Total, summary.Uploaded, summary.Skipped, summary.Failed, opts.ManifestOut);

        return summary.Failed == 0 ? 0 : 1;
    }

    private static async Task<int> RunVerifyAsync(
        IServiceProvider services,
        MigrationCliOptions opts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opts.ManifestIn))
            throw new CliArgumentException("--manifest-in is required for the verify subcommand.");
        if (!File.Exists(opts.ManifestIn))
            throw new CliArgumentException($"Manifest file does not exist: {opts.ManifestIn}");

        using var scope = services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StorageMigration.Verify");

        var verifier = new MigrationVerifier(storage, logger);
        var drift = await verifier.VerifyAsync(opts.ManifestIn!, ct).ConfigureAwait(false);

        if (drift.Count == 0)
        {
            logger.LogInformation("Verify OK: every Uploaded manifest entry is still present in the configured backend.");
            return 0;
        }

        foreach (var miss in drift)
            logger.LogWarning("Drift: missing {Container}/{Key} (legacy {Legacy})", miss.Container, miss.Key, miss.LegacyPath);
        logger.LogError("Verify FAILED: {Count} manifest entries no longer exist in the configured backend.", drift.Count);
        return 1;
    }
}

internal enum MigrationSubcommand
{
    Migrate,
    Verify,
}

internal sealed class CliArgumentException : Exception
{
    public CliArgumentException(string message) : base(message) { }
}

internal sealed class MigrationCliOptions
{
    public const int MaxParallelism = 8;

    public MigrationSubcommand Subcommand { get; init; } = MigrationSubcommand.Migrate;
    public string? LegacyRoot { get; init; }
    public string? ManifestOut { get; init; }
    public string? ManifestIn { get; init; }
    public string Provider { get; init; } = "AzureBlob";
    public string? AccountReference { get; init; }
    public string? ConnectionString { get; init; }
    public string? LocalRoot { get; init; }
    public string? DbConnectionString { get; init; }
    public int Parallelism { get; init; } = 1;
    public bool ShowHelp { get; init; }

    public static MigrationCliOptions Parse(string[] args)
    {
        var subcommand = MigrationSubcommand.Migrate;
        string? legacyRoot = null, manifestOut = null, manifestIn = null;
        string provider = "AzureBlob";
        string? accountRef = null, connStr = null, localRoot = null, dbConn = null;
        int parallelism = 1;
        bool showHelp = false;

        // Allow the first non-flag arg to select the subcommand.
        var i = 0;
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            subcommand = args[0].ToLowerInvariant() switch
            {
                "migrate" => MigrationSubcommand.Migrate,
                "verify" => MigrationSubcommand.Verify,
                _ => throw new CliArgumentException($"unknown subcommand '{args[0]}'."),
            };
            i = 1;
        }

        while (i < args.Length)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    i++;
                    break;
                case "--verify":
                    subcommand = MigrationSubcommand.Verify;
                    i++;
                    break;
                case "--legacy-root":
                    legacyRoot = RequireValue(args, ref i, arg);
                    break;
                case "--manifest-out":
                    manifestOut = RequireValue(args, ref i, arg);
                    break;
                case "--manifest-in":
                    manifestIn = RequireValue(args, ref i, arg);
                    break;
                case "--provider":
                    provider = RequireValue(args, ref i, arg);
                    break;
                case "--account-reference":
                    accountRef = RequireValue(args, ref i, arg);
                    break;
                case "--connection-string":
                    connStr = RequireValue(args, ref i, arg);
                    break;
                case "--local-root":
                    localRoot = RequireValue(args, ref i, arg);
                    break;
                case "--db-connection-string":
                    dbConn = RequireValue(args, ref i, arg);
                    break;
                case "--parallelism":
                    var raw = RequireValue(args, ref i, arg);
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parallelism))
                        throw new CliArgumentException($"--parallelism must be an integer (got '{raw}').");
                    if (parallelism < 1) parallelism = 1;
                    if (parallelism > MaxParallelism) parallelism = MaxParallelism;
                    break;
                default:
                    throw new CliArgumentException($"unknown flag '{arg}'.");
            }
        }

        // Provider validation (mirror StorageOptionsValidator's known list).
        var known = new[] { "AzureBlob", "Azurite", "LocalFilesystem" };
        if (!known.Contains(provider, StringComparer.OrdinalIgnoreCase))
            throw new CliArgumentException(
                $"--provider '{provider}' invalid. Valid: {string.Join(", ", known)}.");

        if (string.Equals(provider, "LocalFilesystem", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(localRoot))
            throw new CliArgumentException("--provider=LocalFilesystem requires --local-root.");

        if ((string.Equals(provider, "AzureBlob", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(provider, "Azurite", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(accountRef) &&
            string.IsNullOrWhiteSpace(connStr))
        {
            throw new CliArgumentException(
                "AzureBlob/Azurite provider requires --account-reference or --connection-string.");
        }

        return new MigrationCliOptions
        {
            Subcommand = subcommand,
            LegacyRoot = legacyRoot,
            ManifestOut = manifestOut,
            ManifestIn = manifestIn,
            Provider = provider,
            AccountReference = accountRef,
            ConnectionString = connStr,
            LocalRoot = localRoot,
            DbConnectionString = dbConn,
            Parallelism = parallelism,
            ShowHelp = showHelp,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new CliArgumentException($"flag '{flag}' requires a value.");
        var value = args[i + 1];
        i += 2;
        return value;
    }

    public const string HelpText = """
Spec 014 storage migration tool (US4 / FR-024).

Usage:
  storage-migration migrate --legacy-root <path> --manifest-out <path>
                            --provider <AzureBlob|Azurite|LocalFilesystem>
                            (--account-reference <name> | --connection-string <conn>)
                            [--local-root <path>]
                            [--db-connection-string <conn>]
                            [--parallelism N]

  storage-migration verify  --manifest-in <path>
                            --provider <AzureBlob|Azurite|LocalFilesystem>
                            (--account-reference <name> | --connection-string <conn>)

Notes:
  - Idempotent: re-running skips entries already present at their computed key.
  - Manifest is JSON Lines; one entry per legacy file (data-model.md § Migration manifest).
  - Single-threaded by default. --parallelism is hard-capped at 8.
  - Read-only against the legacy root; never deletes or moves source files.
""";
}

/// <summary>
/// Looks up each legacy file path in the DB to derive the owning row's
/// (FileCategory, ownerSegment, entityId). Loaded once per run; subsequent
/// lookups hit an in-memory dictionary keyed by absolute path + filename.
/// </summary>
public sealed class LegacyRowResolver
{
    private readonly AppDbContext _db;
    private readonly Dictionary<string, ResolvedRow> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResolvedRow> _byFileName = new(StringComparer.OrdinalIgnoreCase);

    public LegacyRowResolver(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Test seam: build an empty resolver and pre-populate via <see cref="AddManually"/>.</summary>
    public LegacyRowResolver()
    {
        _db = null!;
    }

    /// <summary>Test seam: register a (legacyPath, row) mapping without touching the DB.</summary>
    public void AddManually(string legacyPath, FileCategory category, string ownerSegment, string entityId)
    {
        var row = new ResolvedRow(category, ownerSegment, entityId);
        _byPath[legacyPath] = row;
        var leaf = Path.GetFileName(legacyPath);
        if (!string.IsNullOrEmpty(leaf))
            _byFileName[leaf] = row;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        // SignedUploads — signed-funding-agreements category.
        var signed = await _db.SignedUploads
            .AsNoTracking()
            .Select(s => new { s.Id, s.FundingAgreementId, s.UploaderUserId, s.FileName, s.StoragePath })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var s in signed)
        {
            // Ownership: applicants/{uploaderUserId} (the user who uploaded the signed PDF).
            var owner = $"applicants/{s.UploaderUserId.ToLowerInvariant()}";
            var entity = $"signed-{s.Id}";
            Index(s.StoragePath, s.FileName, FileCategory.SignedFundingAgreement, owner, entity);
        }

        // FundingAgreements — generated-artifacts category (system-generated PDFs).
        var agreements = await _db.FundingAgreements
            .AsNoTracking()
            .Select(a => new { a.Id, a.ApplicationId, a.FileName, a.StoragePath, a.GeneratedByUserId })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var a in agreements)
        {
            var owner = $"applicants/{a.GeneratedByUserId.ToLowerInvariant()}";
            var entity = $"agreement-{a.Id}";
            Index(a.StoragePath, a.FileName, FileCategory.GeneratedArtifact, owner, entity);
        }

        // Documents — application-attachments category.
        var docs = await _db.Documents
            .AsNoTracking()
            .Select(d => new { d.Id, d.OriginalFileName, d.StoragePath })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var d in docs)
        {
            // Documents don't carry their own owner GUID in current dacpac; keys are admin-owned.
            var entity = $"doc-{d.Id}";
            Index(d.StoragePath, d.OriginalFileName, FileCategory.ApplicationAttachment, "admin", entity);
        }
    }

    private void Index(string? storagePath, string fileName, FileCategory category, string owner, string entity)
    {
        var row = new ResolvedRow(category, owner, entity);
        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            _byPath[storagePath] = row;
            // Also index by the filename / last segment to make matching robust when
            // the legacy root in the operator runbook differs from the dacpac value.
            var leaf = Path.GetFileName(storagePath);
            if (!string.IsNullOrEmpty(leaf))
                _byFileName[leaf] = row;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
            _byFileName.TryAdd(fileName, row);
    }

    public ResolvedRow? Resolve(string legacyAbsolutePath)
    {
        if (_byPath.TryGetValue(legacyAbsolutePath, out var byPath))
            return byPath;

        var leaf = Path.GetFileName(legacyAbsolutePath);
        if (!string.IsNullOrEmpty(leaf) && _byFileName.TryGetValue(leaf, out var byLeaf))
            return byLeaf;

        return null;
    }

    public sealed record ResolvedRow(FileCategory Category, string OwnerSegment, string EntityId);
}

public sealed record MigrationSummary(int Total, int Uploaded, int Skipped, int Failed);

public sealed class MigrationRunner
{
    private readonly IObjectStorage _storage;
    private readonly LegacyRowResolver _resolver;
    private readonly ILogger _logger;

    public MigrationRunner(IObjectStorage storage, LegacyRowResolver resolver, ILogger logger)
    {
        _storage = storage;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<MigrationSummary> RunAsync(
        string legacyRoot,
        string manifestOut,
        int parallelism,
        CancellationToken ct)
    {
        if (parallelism < 1) parallelism = 1;
        if (parallelism > MigrationCliOptions.MaxParallelism) parallelism = MigrationCliOptions.MaxParallelism;

        // Snapshot the file list up front so concurrent writes during the run do not
        // surprise us and so we can iterate deterministically.
        var allFiles = Directory
            .EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        await using var writer = new MigrationManifest.Writer(manifestOut);

        int uploaded = 0, skipped = 0, failed = 0;

        if (parallelism == 1)
        {
            foreach (var file in allFiles)
            {
                var entry = await ProcessOneAsync(file, ct).ConfigureAwait(false);
                await writer.AppendAsync(entry, ct).ConfigureAwait(false);
                Tally(entry, ref uploaded, ref skipped, ref failed);
            }
        }
        else
        {
            using var gate = new SemaphoreSlim(parallelism, parallelism);
            var tasks = allFiles.Select(async file =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var entry = await ProcessOneAsync(file, ct).ConfigureAwait(false);
                    await writer.AppendAsync(entry, ct).ConfigureAwait(false);
                    return entry;
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            foreach (var t in tasks)
            {
                var entry = await t.ConfigureAwait(false);
                Tally(entry, ref uploaded, ref skipped, ref failed);
            }
        }

        return new MigrationSummary(allFiles.Count, uploaded, skipped, failed);
    }

    private static void Tally(MigrationManifestEntry entry, ref int uploaded, ref int skipped, ref int failed)
    {
        switch (entry.Outcome)
        {
            case MigrationManifest.OutcomeNames.Uploaded: uploaded++; break;
            case MigrationManifest.OutcomeNames.SkippedExisting: skipped++; break;
            default: failed++; break;
        }
    }

    private async Task<MigrationManifestEntry> ProcessOneAsync(string legacyPath, CancellationToken ct)
    {
        var resolved = _resolver.Resolve(legacyPath);
        if (resolved is null)
        {
            return new MigrationManifestEntry
            {
                LegacyPath = legacyPath,
                Category = "Unknown",
                OwnerSegment = "",
                EntityId = "",
                DeterministicSuffix = "",
                Extension = Path.GetExtension(legacyPath).ToLowerInvariant(),
                ComputedKey = "",
                Outcome = MigrationManifest.OutcomeNames.Failed,
                SizeBytes = 0,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = $"No DB row found for legacy path '{legacyPath}'.",
            };
        }

        var suffix = ComputeDeterministicSuffix(legacyPath);
        var ext = Path.GetExtension(legacyPath);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";

        ObjectKey key;
        try
        {
            key = ObjectKey.Build(resolved.Category, resolved.OwnerSegment, resolved.EntityId, suffix, ext);
        }
        catch (Exception ex)
        {
            return new MigrationManifestEntry
            {
                LegacyPath = legacyPath,
                Category = resolved.Category.ToString(),
                OwnerSegment = resolved.OwnerSegment,
                EntityId = resolved.EntityId,
                DeterministicSuffix = suffix,
                Extension = ext.ToLowerInvariant(),
                ComputedKey = "",
                Outcome = MigrationManifest.OutcomeNames.Failed,
                SizeBytes = 0,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = $"ObjectKey.Build failed: {ex.Message}",
            };
        }

        var fi = new FileInfo(legacyPath);
        var size = fi.Exists ? fi.Length : 0L;
        var contentType = GuessContentType(ext);
        var category = resolved.Category;

        try
        {
            // Idempotent: ExistsAsync first, skip if present (research.md §R6).
            if (await _storage.ExistsAsync(category, key, ct).ConfigureAwait(false))
            {
                return new MigrationManifestEntry
                {
                    LegacyPath = legacyPath,
                    Category = category.ToString(),
                    OwnerSegment = resolved.OwnerSegment,
                    EntityId = resolved.EntityId,
                    DeterministicSuffix = suffix,
                    Extension = ext.ToLowerInvariant(),
                    ComputedKey = key.Value,
                    Outcome = MigrationManifest.OutcomeNames.SkippedExisting,
                    SizeBytes = size,
                    CompletedAt = DateTimeOffset.UtcNow,
                };
            }

            await using (var stream = new FileStream(legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await _storage.UploadAsync(category, key, stream, contentType, size, ct).ConfigureAwait(false);
            }

            // Verify after upload (R6 atomicity check).
            var verify = await _storage.ExistsAsync(category, key, ct).ConfigureAwait(false);
            if (!verify)
                throw new InvalidOperationException(
                    "Upload succeeded but ExistsAsync still reports false — backend drift.");

            return new MigrationManifestEntry
            {
                LegacyPath = legacyPath,
                Category = category.ToString(),
                OwnerSegment = resolved.OwnerSegment,
                EntityId = resolved.EntityId,
                DeterministicSuffix = suffix,
                Extension = ext.ToLowerInvariant(),
                ComputedKey = key.Value,
                Outcome = MigrationManifest.OutcomeNames.Uploaded,
                SizeBytes = size,
                CompletedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Migration failed for {Path}", legacyPath);
            return new MigrationManifestEntry
            {
                LegacyPath = legacyPath,
                Category = category.ToString(),
                OwnerSegment = resolved.OwnerSegment,
                EntityId = resolved.EntityId,
                DeterministicSuffix = suffix,
                Extension = ext.ToLowerInvariant(),
                ComputedKey = key.Value,
                Outcome = MigrationManifest.OutcomeNames.Failed,
                SizeBytes = size,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = $"{ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    /// <summary>FR-014 / R3: SHA-256 of the absolute legacy path, first 16 hex chars.</summary>
    public static string ComputeDeterministicSuffix(string legacyAbsolutePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(legacyAbsolutePath));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static string GuessContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".csv" => "text/csv",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}

public sealed record VerifyDrift(string Container, string Key, string LegacyPath);

public sealed class MigrationVerifier
{
    private readonly IObjectStorage _storage;
    private readonly ILogger _logger;

    public MigrationVerifier(IObjectStorage storage, ILogger logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VerifyDrift>> VerifyAsync(string manifestPath, CancellationToken ct)
    {
        var drift = new List<VerifyDrift>();
        await foreach (var entry in MigrationManifest.ReadAsync(manifestPath, ct).ConfigureAwait(false))
        {
            if (entry.Outcome != MigrationManifest.OutcomeNames.Uploaded)
                continue;

            if (string.IsNullOrWhiteSpace(entry.ComputedKey))
                continue;

            // Reconstruct the FileCategory + ObjectKey from the manifest fields.
            if (!Enum.TryParse<FileCategory>(entry.Category, ignoreCase: true, out var category))
            {
                drift.Add(new VerifyDrift(entry.ComputedKey.Split('/')[0], entry.ComputedKey, entry.LegacyPath));
                continue;
            }

            var key = ObjectKey.Parse(entry.ComputedKey);
            var exists = await _storage.ExistsAsync(category, key, ct).ConfigureAwait(false);
            if (!exists)
                drift.Add(new VerifyDrift(key.Container, key.Value, entry.LegacyPath));
        }
        return drift;
    }
}
