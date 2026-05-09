using System.Text;
using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Application.Options;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Commands;
using FundingPlatform.Application.SignedUploads.Queries;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using FundingPlatform.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Integration.Web;

/// <summary>
/// Service-level integration tests for SignedUploadService. Mirrors the lightweight
/// in-memory-EF pattern established by <see cref="FundingAgreementEndpointsTests"/>.
/// </summary>
[TestFixture]
public class SignedUploadEndpointsTests
{
    private AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    private static SignedUploadService BuildService(AppDbContext ctx, IObjectStorage? storage = null)
    {
        var appRepo = new ApplicationRepository(ctx);
        var suRepo = new SignedUploadRepository(ctx);
        var options = Options.Create(new SignedUploadOptions { MaxSizeBytes = 20L * 1024 * 1024 });
        storage ??= new InMemoryFileStorage();
        return new SignedUploadService(
            appRepo, suRepo, storage, options, NullLogger<SignedUploadService>.Instance);
    }

    private static Stream PdfStream(string body = "fake PDF body")
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n" + body);
        return new MemoryStream(bytes);
    }

    [Test]
    public async Task US1_UploadThenApprove_Succeeds()
    {
        var dbName = $"su-upload-{Guid.NewGuid():N}";
        int appId;
        string applicantUserId;

        using (var ctx = CreateContext(dbName))
        {
            var (app, userId) = SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;
        }

        // Upload
        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            await using var stream = PdfStream();
            var result = await service.UploadAsync(new UploadSignedAgreementCommand(
                ApplicationId: appId,
                UserId: applicantUserId,
                GeneratedVersion: 1,
                FileName: "signed.pdf",
                ContentType: "application/pdf",
                Size: stream.Length,
                Content: stream));

            Assert.That(result.Success, Is.True, result.Error?.ToString());
            Assert.That(result.SignedUploadId, Is.Not.Null);
        }

        int pendingId;
        using (var ctx = CreateContext(dbName))
        {
            var upload = await ctx.SignedUploads
                .FirstOrDefaultAsync(u => u.FundingAgreementId ==
                    ctx.FundingAgreements.First(fa => fa.ApplicationId == appId).Id);
            Assert.That(upload, Is.Not.Null);
            pendingId = upload!.Id;
        }

        // Approve
        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            var result = await service.ApproveAsync(new ApproveSignedUploadCommand(
                ApplicationId: appId,
                ReviewerUserId: "reviewer-1",
                IsAdministrator: true,
                IsReviewerAssigned: false,
                SignedUploadId: pendingId,
                Comment: "ok"));

            Assert.That(result.Success, Is.True, result.Error?.ToString());
        }

        using (var ctx = CreateContext(dbName))
        {
            var app = await ctx.Applications
                .Include(a => a.VersionHistory)
                .FirstAsync(a => a.Id == appId);

            Assert.That(app.State, Is.EqualTo(ApplicationState.AgreementExecuted));

            var actions = app.VersionHistory.Select(v => v.Action).ToList();
            Assert.That(actions, Does.Contain(SigningAuditActions.SignedAgreementUploaded));
            Assert.That(actions, Does.Contain(SigningAuditActions.SignedUploadApproved));
        }
    }

    [Test]
    public async Task US1_Upload_WithNonPdfContentType_Returns400WithoutCreatingRecord()
    {
        var dbName = $"su-nonpdf-{Guid.NewGuid():N}";
        int appId;
        string applicantUserId;

        using (var ctx = CreateContext(dbName))
        {
            var (app, userId) = SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            var bytes = Encoding.ASCII.GetBytes("not a pdf at all");
            await using var stream = new MemoryStream(bytes);

            var result = await service.UploadAsync(new UploadSignedAgreementCommand(
                ApplicationId: appId,
                UserId: applicantUserId,
                GeneratedVersion: 1,
                FileName: "cover.docx",
                ContentType: "application/msword",
                Size: stream.Length,
                Content: stream));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError, Is.True);
        }

        using (var ctx = CreateContext(dbName))
        {
            var count = await ctx.SignedUploads.CountAsync();
            Assert.That(count, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task US1_Upload_WithRenamedNonPdf_Returns400()
    {
        var dbName = $"su-mag-{Guid.NewGuid():N}";
        int appId;
        string applicantUserId;

        using (var ctx = CreateContext(dbName))
        {
            var (app, userId) = SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            // Claims application/pdf but content is not %PDF-
            var bytes = Encoding.ASCII.GetBytes("not-a-pdf-prefix-but-claims-to-be");
            await using var stream = new MemoryStream(bytes);

            var result = await service.UploadAsync(new UploadSignedAgreementCommand(
                ApplicationId: appId,
                UserId: applicantUserId,
                GeneratedVersion: 1,
                FileName: "cover.pdf",
                ContentType: "application/pdf",
                Size: stream.Length,
                Content: stream));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError, Is.True);
            // Spec 012 — validation now surfaces a code instead of the English
            // sentinel string the previous implementation returned. Either of
            // the PDF-specific codes is acceptable for this case.
            Assert.That(result.Error?.Code,
                Is.EqualTo(FundingPlatform.Application.Errors.UserFacingErrorCode.SignedUploadNotAPdf)
                    .Or.EqualTo(FundingPlatform.Application.Errors.UserFacingErrorCode.SignedUploadMissingPdfHeader));
        }

        using (var ctx = CreateContext(dbName))
        {
            var count = await ctx.SignedUploads.CountAsync();
            Assert.That(count, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task US1_DownloadSigned_AsApplicantOwner_AuthorizedWhenAgreementExecuted()
    {
        var dbName = $"su-dl-{Guid.NewGuid():N}";
        int appId;
        string applicantUserId;
        int pendingId;
        var storage = new InMemoryFileStorage();

        using (var ctx = CreateContext(dbName))
        {
            var (app, userId) = SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx, storage);
            await using var stream = PdfStream();
            var result = await service.UploadAsync(new UploadSignedAgreementCommand(
                appId, applicantUserId, 1, "signed.pdf", "application/pdf", stream.Length, stream));
            pendingId = result.SignedUploadId!.Value;
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx, storage);
            await service.ApproveAsync(new ApproveSignedUploadCommand(
                appId, "admin-1", true, false, pendingId, null));
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx, storage);
            var result = await service.GetDownloadAsync(new GetSignedAgreementDownloadQuery(
                ApplicationId: appId,
                SignedUploadId: pendingId,
                UserId: applicantUserId,
                IsAdministrator: false,
                IsReviewerAssigned: false));

            Assert.That(result.Authorized, Is.True);
            Assert.That(result.Content, Is.Not.Null);
        }
    }

    [Test]
    public async Task US2_Reject_WithoutComment_Returns400WithValidationError()
    {
        var dbName = $"su-reject-noc-{Guid.NewGuid():N}";
        var (appId, applicantUserId, pendingId) = await SeedPendingUploadAsync(dbName);

        using var ctx = CreateContext(dbName);
        var service = BuildService(ctx);

        var result = await service.RejectAsync(new RejectSignedUploadCommand(
            ApplicationId: appId,
            ReviewerUserId: "admin-1",
            IsAdministrator: true,
            IsReviewerAssigned: false,
            SignedUploadId: pendingId,
            Comment: ""));

        Assert.That(result.Success, Is.False);
        Assert.That(result.ValidationError, Is.True);
        Assert.That(result.Error?.Code,
            Is.EqualTo(FundingPlatform.Application.Errors.UserFacingErrorCode.SignedUploadRejectionCommentRequired));
    }

    [Test]
    public async Task US2_Reject_WithComment_TransitionsUploadAndAppendsAudit()
    {
        var dbName = $"su-reject-ok-{Guid.NewGuid():N}";
        var (appId, _, pendingId) = await SeedPendingUploadAsync(dbName);

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            var result = await service.RejectAsync(new RejectSignedUploadCommand(
                ApplicationId: appId,
                ReviewerUserId: "admin-1",
                IsAdministrator: true,
                IsReviewerAssigned: false,
                SignedUploadId: pendingId,
                Comment: "signature illegible"));

            Assert.That(result.Success, Is.True, result.Error?.ToString());
        }

        using (var ctx = CreateContext(dbName))
        {
            var upload = await ctx.SignedUploads
                .Include(u => u.ReviewDecision)
                .FirstAsync(u => u.Id == pendingId);

            Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Rejected));
            Assert.That(upload.ReviewDecision, Is.Not.Null);
            Assert.That(upload.ReviewDecision!.Outcome, Is.EqualTo(SigningDecisionOutcome.Rejected));
            Assert.That(upload.ReviewDecision.Comment, Is.EqualTo("signature illegible"));

            var app = await ctx.Applications
                .Include(a => a.VersionHistory)
                .FirstAsync(a => a.Id == appId);
            Assert.That(app.State, Is.EqualTo(ApplicationState.ResponseFinalized),
                "Rejection must not transition the application state.");
            Assert.That(app.VersionHistory.Select(v => v.Action),
                Does.Contain(SigningAuditActions.SignedUploadRejected));
        }
    }

    [Test]
    public async Task US3_ReplacePendingUpload_SupersedesPriorAndKeepsAudit()
    {
        var dbName = $"su-replace-{Guid.NewGuid():N}";
        var (appId, applicantUserId, pendingId) = await SeedPendingUploadAsync(dbName);

        int newPendingId;
        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            await using var stream = PdfStream("different");
            var result = await service.ReplaceAsync(new ReplaceSignedUploadCommand(
                ApplicationId: appId,
                UserId: applicantUserId,
                SignedUploadId: pendingId,
                GeneratedVersion: 1,
                FileName: "revised.pdf",
                ContentType: "application/pdf",
                Size: stream.Length,
                Content: stream));

            Assert.That(result.Success, Is.True, result.Error?.ToString());
            Assert.That(result.SignedUploadId, Is.Not.EqualTo(pendingId));
            newPendingId = result.SignedUploadId!.Value;
        }

        using (var ctx = CreateContext(dbName))
        {
            var uploads = await ctx.SignedUploads
                .Where(u => u.Id == pendingId || u.Id == newPendingId)
                .ToListAsync();

            var prior = uploads.Single(u => u.Id == pendingId);
            var current = uploads.Single(u => u.Id == newPendingId);

            Assert.That(prior.Status, Is.EqualTo(SignedUploadStatus.Superseded));
            Assert.That(current.Status, Is.EqualTo(SignedUploadStatus.Pending));
        }
    }

    [Test]
    public async Task US3_WithdrawPendingUpload_ReturnsToReady()
    {
        var dbName = $"su-withdraw-{Guid.NewGuid():N}";
        var (appId, applicantUserId, pendingId) = await SeedPendingUploadAsync(dbName);

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            var result = await service.WithdrawAsync(new WithdrawSignedUploadCommand(
                appId, applicantUserId, pendingId));
            Assert.That(result.Success, Is.True, result.Error?.ToString());
        }

        using (var ctx = CreateContext(dbName))
        {
            var upload = await ctx.SignedUploads.FirstAsync(u => u.Id == pendingId);
            Assert.That(upload.Status, Is.EqualTo(SignedUploadStatus.Withdrawn));

            var app = await ctx.Applications
                .Include(a => a.VersionHistory)
                .FirstAsync(a => a.Id == appId);
            Assert.That(app.VersionHistory.Select(v => v.Action),
                Does.Contain(SigningAuditActions.SignedUploadWithdrawn));
        }
    }

    [Test]
    public async Task US4_Upload_WithStaleGeneratedVersion_Returns400WithoutCreatingRecord()
    {
        var dbName = $"su-stale-{Guid.NewGuid():N}";
        int appId;
        string applicantUserId;

        using (var ctx = CreateContext(dbName))
        {
            var (app, userId) = SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;

            // Regenerate to advance GeneratedVersion to 2
            app.RegenerateFundingAgreement("v2.pdf", "application/pdf", 1, "/store/v2.pdf", "admin-1");
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            await using var stream = PdfStream();
            // Upload claims version 1, but current is 2
            var result = await service.UploadAsync(new UploadSignedAgreementCommand(
                appId, applicantUserId, 1, "stale.pdf", "application/pdf", stream.Length, stream));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError, Is.True);
            Assert.That(result.Error?.Code,
                Is.EqualTo(FundingPlatform.Application.Errors.UserFacingErrorCode.SignedUploadStaleAgreementVersion));
        }

        using (var ctx = CreateContext(dbName))
        {
            var count = await ctx.SignedUploads.CountAsync();
            Assert.That(count, Is.EqualTo(0));
        }
    }

    private async Task<(int appId, string applicantUserId, int pendingId)> SeedPendingUploadAsync(string dbName)
    {
        int appId;
        string applicantUserId;
        int pendingId;

        using (var ctx = CreateContext(dbName))
        {
            var (app, userId) = SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;
        }

        using (var ctx = CreateContext(dbName))
        {
            var service = BuildService(ctx);
            await using var stream = PdfStream();
            var result = await service.UploadAsync(new UploadSignedAgreementCommand(
                appId, applicantUserId, 1, "signed.pdf", "application/pdf", stream.Length, stream));
            pendingId = result.SignedUploadId!.Value;
        }

        return (appId, applicantUserId, pendingId);
    }

    private static (AppEntity Application, string ApplicantUserId) SeedAcceptedApplicationWithAgreement(AppDbContext ctx)
    {
        var uniq = Guid.NewGuid().ToString("N");
        var applicantUserId = $"user-{uniq}";
        var applicant = new Applicant(
            userId: applicantUserId,
            legalId: "LEG-1",
            firstName: "Ana",
            lastName: "Applicant",
            email: $"ana-{uniq}@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        ctx.SaveChanges();

        var category = new Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        ctx.SaveChanges();

        var application = new AppEntity(applicant.Id, "Test Company");
        application.AddItem(new Item("Laptop", category.Id, "specs"));
        typeof(AppEntity).GetProperty("State")!.SetValue(application, ApplicationState.Resolved);
        ctx.Applications.Add(application);
        ctx.SaveChanges();

        var itemId = application.Items[0].Id;
        application.SubmitResponse(
            new Dictionary<int, ItemResponseDecision> { [itemId] = ItemResponseDecision.Accept },
            submittedByUserId: applicant.UserId);

        application.GenerateFundingAgreement(
            "agreement.pdf", "application/pdf", 1024, "/store/agreement.pdf", "admin-1");

        ctx.SaveChanges();
        return (application, applicantUserId);
    }

    private sealed class InMemoryFileStorage : IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public async Task<StoredObject> UploadAsync(
            FileCategory category, ObjectKey key, Stream content, string contentType,
            long? contentLength, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            _store[key.Value] = ms.ToArray();
            return new StoredObject(
                Container: key.Container, Key: key.Value, SizeBytes: ms.Length,
                ContentType: contentType, CreatedAt: DateTimeOffset.UtcNow,
                Provider: StorageProviderName.LocalFilesystem);
        }

        public Task<Stream> OpenReadAsync(FileCategory category, ObjectKey key, CancellationToken ct)
        {
            if (!_store.TryGetValue(key.Value, out var bytes))
                throw new ObjectNotFoundException(key.Container, key.Value);
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task<bool> ExistsAsync(FileCategory category, ObjectKey key, CancellationToken ct)
            => Task.FromResult(_store.ContainsKey(key.Value));

        public Task DeleteAsync(FileCategory category, ObjectKey key, CancellationToken ct)
        {
            _store.Remove(key.Value);
            return Task.CompletedTask;
        }

        public Task<StorageHandle> ResolveServingHandleAsync(
            FileCategory category, ObjectKey key, ServingMode preferred, CancellationToken ct)
        {
            if (!_store.TryGetValue(key.Value, out var bytes))
                throw new ObjectNotFoundException(key.Container, key.Value);
            return Task.FromResult<StorageHandle>(new BackendStreamHandle(
                new MemoryStream(bytes), "application/pdf", bytes.Length));
        }
    }
}
