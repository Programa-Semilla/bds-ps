// Spec 021 — see specs/021-feedback-session-may13/research.md R-5
// and contracts/applicant-routes.md (POST /api/applications/{publicCode}/autosave).

using FundingPlatform.Application.Applications;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / T090 / R-5 / FR-016 — EF-backed implementation of
/// <see cref="IAutosaveFieldHandler"/>. Looks up the Application by
/// <c>PublicCode</c>, asserts the stage window is open (raises
/// <see cref="StageWindowClosedException"/> → mapped to HTTP 422), checks the
/// ETag, applies the supplied <c>fieldKey → value</c> mutation, saves, and
/// returns the new ETag + timestamp.
/// </summary>
public sealed class AutosaveFieldHandler : IAutosaveFieldHandler
{
    private readonly AppDbContext _db;
    private readonly IStageExpiryClock _clock;

    public AutosaveFieldHandler(AppDbContext db, IStageExpiryClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AutosaveFieldResult> HandleAsync(
        AutosaveFieldCommand cmd, int currentApplicantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.PublicCode))
        {
            throw new ArgumentException("PublicCode is required.", nameof(cmd));
        }

        var publicCode = cmd.PublicCode.Trim().ToUpperInvariant();
        var application = await _db.Applications
            .FirstOrDefaultAsync(a => EF.Property<string>(a, "PublicCode") == publicCode, ct)
            ?? throw new InvalidOperationException(
                $"Application with PublicCode '{publicCode}' not found.");

        if (application.ApplicantId != currentApplicantId)
        {
            throw new UnauthorizedAccessException(
                "Caller does not own the application identified by the supplied PublicCode.");
        }

        // R-5 — stage-window guard. The stage-closed exception bubbles to the
        // global DomainExceptionFilter which maps it to HTTP 422 with the
        // es-CR ProblemDetails copy.
        var stageClosesAt = await ResolveSolicitudCloseInstantAsync(application, ct);
        var now = _clock.UtcNow;
        if (now >= stageClosesAt)
        {
            throw new StageWindowClosedException(StageKind.Solicitud, stageClosesAt);
        }

        // ETag check — Application.RowVersion is the optimistic-concurrency
        // token. We compare the supplied base64 string against the stored bytes.
        if (cmd.Etag is { Length: > 0 } supplied)
        {
            var current = Convert.ToBase64String(application.RowVersion ?? Array.Empty<byte>());
            if (!string.Equals(current, supplied, StringComparison.Ordinal))
            {
                throw new AutosaveConflictException();
            }
        }

        ApplyFieldMutation(application, cmd.FieldKey, cmd.Value);

        await _db.SaveChangesAsync(ct);

        var newEtag = Convert.ToBase64String(application.RowVersion ?? Array.Empty<byte>());
        return new AutosaveFieldResult(newEtag, _clock.UtcNow);
    }

    private static void ApplyFieldMutation(AppEntity application, string fieldKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            throw new ArgumentException("FieldKey is required.", nameof(fieldKey));
        }

        switch (fieldKey)
        {
            case "CompanyName":
                application.SetCompanyName(value ?? string.Empty);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown autosave field-key: '{fieldKey}'.", nameof(fieldKey));
        }
    }

    private async Task<DateTimeOffset> ResolveSolicitudCloseInstantAsync(AppEntity application, CancellationToken ct)
    {
        var config = await _db.SystemConfigurations
            .FirstOrDefaultAsync(c => c.Key == "Stage.Solicitud.WindowDays", ct);
        var days = 14;
        if (config is not null && int.TryParse(config.Value, out var parsed) && parsed > 0)
        {
            days = parsed;
        }
        return application.StageEnteredAt.AddDays(days);
    }
}
