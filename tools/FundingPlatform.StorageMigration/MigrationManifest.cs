using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FundingPlatform.StorageMigration;

/// <summary>
/// JSON Lines append-only manifest writer + reader for the spec 014 migration tool.
/// Schema matches <c>data-model.md § Migration manifest</c>: one entry per legacy file,
/// one line per entry, camelCase fields, UTF-8.
/// </summary>
public static class MigrationManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    /// <summary>Stable name used in the JSONL <c>outcome</c> field. Must match the spec.</summary>
    public static class OutcomeNames
    {
        public const string Uploaded = "Uploaded";
        public const string SkippedExisting = "Skipped-Existing";
        public const string Failed = "Failed";
    }

    /// <summary>Append-only writer. Each call writes a single line; flushed eagerly.</summary>
    public sealed class Writer : IAsyncDisposable, IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public Writer(string path)
        {
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            // Append mode so reruns / partial runs aren't truncated.
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = false,
                NewLine = "\n",
            };
            Path = path;
        }

        public string Path { get; }

        public async Task AppendAsync(MigrationManifestEntry entry, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                await _writer.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            _writer.Dispose();
            _gate.Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _writer.Dispose();
            _gate.Dispose();
        }
    }

    /// <summary>Streaming reader; yields one entry per line.</summary>
    public static async IAsyncEnumerable<MigrationManifestEntry> ReadAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(path))
            yield break;

        using var reader = new StreamReader(path, new System.Text.UTF8Encoding(false));
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var entry = JsonSerializer.Deserialize<MigrationManifestEntry>(line, JsonOptions)
                ?? throw new InvalidDataException($"Manifest line could not be parsed: {line}");
            yield return entry;
        }
    }
}

/// <summary>One JSONL row in the migration manifest. Field names match data-model.md.</summary>
public sealed record MigrationManifestEntry
{
    public required string LegacyPath { get; init; }
    public required string Category { get; init; }
    public required string OwnerSegment { get; init; }
    public required string EntityId { get; init; }
    public required string DeterministicSuffix { get; init; }
    public required string Extension { get; init; }
    public required string ComputedKey { get; init; }
    public required string Outcome { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public string? Error { get; init; }
}
