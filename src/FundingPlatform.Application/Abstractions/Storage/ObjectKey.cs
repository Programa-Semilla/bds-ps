using System.Text.RegularExpressions;

namespace FundingPlatform.Application.Abstractions.Storage;

/// <summary>
/// Canonical object key value object. Implements FR-014:
/// <c>{category}/{owner-segment}/{entity-id}/{deterministic-suffix}.{ext}</c>
/// </summary>
public sealed partial record ObjectKey
{
    public const int MaxLengthBytes = 1024;

    public string Container { get; }
    public string OwnerSegment { get; }
    public string EntityId { get; }
    public string DeterministicSuffix { get; }
    public string Extension { get; }

    /// <summary>Full canonical path (no leading slash). Equivalent to the blob key.</summary>
    public string Value { get; }

    private ObjectKey(
        string container,
        string ownerSegment,
        string entityId,
        string deterministicSuffix,
        string extension,
        string value)
    {
        Container = container;
        OwnerSegment = ownerSegment;
        EntityId = entityId;
        DeterministicSuffix = deterministicSuffix;
        Extension = extension;
        Value = value;
    }

    public static ObjectKey Build(
        FileCategory category,
        string ownerSegment,
        string entityId,
        string deterministicSuffix,
        string? extension)
    {
        var container = category.ContainerName();
        var owner = NormalizeOwner(ownerSegment);
        var entity = NormalizeRequired(entityId, nameof(entityId));
        var suffix = NormalizeRequired(deterministicSuffix, nameof(deterministicSuffix));
        var ext = NormalizeExtension(extension);

        var value = $"{container}/{owner}/{entity}/{suffix}{ext}";
        ValidateValue(value);
        return new ObjectKey(container, owner, entity, suffix, ext, value);
    }

    public static ObjectKey Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Object key must not be blank.", nameof(raw));

        ValidateValue(raw);

        // Split: container / ...rest... / suffix.ext
        var parts = raw.Split('/');
        if (parts.Length < 4)
            throw new ArgumentException(
                $"Object key '{raw}' does not match the canonical format `{{category}}/{{owner-segment}}/{{entity-id}}/{{suffix}}.{{ext}}`.",
                nameof(raw));

        var container = parts[0];
        var fileSegment = parts[^1];
        var entityId = parts[^2];
        var ownerSegment = string.Join('/', parts[1..^2]);

        var dot = fileSegment.LastIndexOf('.');
        var (suffix, ext) = dot >= 0
            ? (fileSegment[..dot], fileSegment[dot..])
            : (fileSegment, string.Empty);

        return new ObjectKey(container, ownerSegment, entityId, suffix, ext, raw);
    }

    public override string ToString() => Value;

    private static string NormalizeOwner(string ownerSegment)
    {
        if (string.IsNullOrWhiteSpace(ownerSegment))
            throw new ArgumentException("Owner segment must not be blank.", nameof(ownerSegment));
        var normalized = ownerSegment.Trim('/').ToLowerInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("Owner segment must not be blank.", nameof(ownerSegment));
        if (normalized.Contains(".."))
            throw new ArgumentException("Owner segment must not contain `..`.", nameof(ownerSegment));
        return normalized;
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} must not be blank.", name);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains('/'))
            throw new ArgumentException($"{name} must not contain '/'.", name);
        if (normalized.Contains(".."))
            throw new ArgumentException($"{name} must not contain '..'.", name);
        return normalized;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ".bin";
        var ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        if (ext.Contains('/') || ext.Contains(".."))
            throw new ArgumentException("Extension must not contain '/' or '..'.", nameof(extension));
        if (!ExtensionPattern().IsMatch(ext))
            throw new ArgumentException($"Extension '{ext}' contains invalid characters.", nameof(extension));
        return ext;
    }

    private static void ValidateValue(string value)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value) > MaxLengthBytes)
            throw new ArgumentException(
                $"Object key length {value.Length} exceeds the {MaxLengthBytes}-byte cap.",
                nameof(value));
        if (value.Contains(".."))
            throw new ArgumentException("Object key must not contain '..'.", nameof(value));
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
                throw new ArgumentException("Object key must not contain control characters.", nameof(value));
        }
        // The container portion (first segment) must be lowercase per Azure naming rules.
        var firstSlash = value.IndexOf('/');
        if (firstSlash <= 0)
            throw new ArgumentException("Object key must include a container segment.", nameof(value));
        var container = value[..firstSlash];
        if (container != container.ToLowerInvariant())
            throw new ArgumentException("Container segment must be lowercase.", nameof(value));
    }

    [GeneratedRegex(@"^\.[a-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();
}
