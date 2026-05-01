using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Services;
using FundingPlatform.Application.SignedUploads.Commands;
using FundingPlatform.Web.Localization;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Tests.Integration.Web;

/// <summary>
/// Spec 012 / FR-014 — verifies that Application-layer
/// <see cref="UserFacingErrorCode"/> values raised by service methods are
/// rendered as Spanish (es-CR) strings by <see cref="UserFacingErrorTranslator"/>
/// before the message reaches the user via TempData / ModelState.
///
/// <para>NFR-001 invariant: Application services raise codes (English-named
/// enum values, English-only logs); only the Web translator emits Spanish.</para>
/// </summary>
[TestFixture]
public class UserFacingErrorTranslationTests
{
    private IUserFacingErrorTranslator _translator = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _translator = new UserFacingErrorTranslator();
    }

    [TestCase(UserFacingErrorCode.ApplicationNotFound, "Solicitud no encontrada.")]
    [TestCase(UserFacingErrorCode.ApplicationNotUnderReview, "La solicitud no está en revisión.")]
    [TestCase(UserFacingErrorCode.ApplicationItemNotFound, "Ítem no encontrado en la solicitud.")]
    [TestCase(UserFacingErrorCode.ApplicationNotOwnedByApplicant, "Usted no es el dueño de esta solicitud.")]
    [TestCase(UserFacingErrorCode.SupplierRequiredOnApprove, "Debe seleccionar un proveedor para aprobar el ítem.")]
    [TestCase(UserFacingErrorCode.ConcurrentApplicationModification,
        "Otro usuario modificó esta solicitud. Refresque la página e inténtelo nuevamente.")]
    [TestCase(UserFacingErrorCode.AppealAccessDenied, "Usted no tiene acceso a esta apelación.")]
    [TestCase(UserFacingErrorCode.NoOpenAppealForMessage, "No hay una apelación abierta para responder.")]
    [TestCase(UserFacingErrorCode.ConcurrentAppealModification,
        "Otro usuario modificó esta apelación. Refresque la página e inténtelo nuevamente.")]
    [TestCase(UserFacingErrorCode.AgreementGenerationPreconditionsNotMet,
        "No se cumplen las precondiciones del convenio de financiamiento.")]
    [TestCase(UserFacingErrorCode.AgreementGenerationFailed,
        "Falló la generación del convenio de financiamiento.")]
    [TestCase(UserFacingErrorCode.AgreementPdfRenderingFailed,
        "No se pudo generar el convenio. Inténtelo nuevamente o contacte al soporte.")]
    [TestCase(UserFacingErrorCode.SignedUploadStaleAgreementVersion,
        "Descargue nuevamente el convenio más reciente y fírmelo otra vez.")]
    [TestCase(UserFacingErrorCode.SignedUploadAlreadyPending,
        "Ya existe una carga firmada pendiente. Use Reemplazar.")]
    [TestCase(UserFacingErrorCode.SignedUploadRejectionCommentRequired,
        "Se requiere un comentario para rechazar la carga.")]
    [TestCase(UserFacingErrorCode.SignedUploadUnsupportedContentType,
        "Solo se aceptan archivos PDF (application/pdf).")]
    [TestCase(UserFacingErrorCode.SignedUploadNotAPdf,
        "El archivo cargado no parece ser un PDF.")]
    public void Translate_RendersSpanishStringForKnownCode(UserFacingErrorCode code, string expected)
    {
        Assert.That(_translator.Translate(code), Is.EqualTo(expected));
    }

    [Test]
    public void Translate_OperationRejected_ReturnsGenericSpanish_AndDoesNotLeakEnglishDetail()
    {
        var error = UserFacingError.From(
            UserFacingErrorCode.OperationRejected,
            "Domain says: Item must be approved before review.");

        var translated = _translator.Translate(error);

        // FR-14 / NFR-001: the English domain detail must NOT reach the user.
        Assert.That(translated, Does.Not.Contain("Domain says"));
        Assert.That(translated, Does.Not.Contain("Item must be"));
        // The user-visible string is the generic Spanish phrasing.
        Assert.That(translated, Is.EqualTo(
            "La operación no se pudo completar. Inténtelo nuevamente o contacte al soporte."));
    }

    [Test]
    public async Task SignedUploadService_NonPdfUpload_ReturnsCode_TranslatedToSpanish()
    {
        // Drives the full Application -> Web translation seam end-to-end.
        var dbName = $"trans-nonpdf-{Guid.NewGuid():N}";
        int appId;
        string applicantUserId;

        using (var ctx = SignedUploadEndpointsTestSeeder.CreateContext(dbName))
        {
            var (app, userId) = SignedUploadEndpointsTestSeeder.SeedAcceptedApplicationWithAgreement(ctx);
            await ctx.SaveChangesAsync();
            appId = app.Id;
            applicantUserId = userId;
        }

        SignedUploadResult result;
        using (var ctx = SignedUploadEndpointsTestSeeder.CreateContext(dbName))
        {
            var service = SignedUploadEndpointsTestSeeder.BuildService(ctx);
            var bytes = System.Text.Encoding.ASCII.GetBytes("not a pdf at all");
            await using var stream = new MemoryStream(bytes);

            result = await service.UploadAsync(new UploadSignedAgreementCommand(
                ApplicationId: appId,
                UserId: applicantUserId,
                GeneratedVersion: 1,
                FileName: "cover.docx",
                ContentType: "application/msword",
                Size: stream.Length,
                Content: stream));
        }

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code,
            Is.EqualTo(UserFacingErrorCode.SignedUploadUnsupportedContentType));

        // Translator emits Spanish.
        var spanish = _translator.Translate(result.Error);
        Assert.That(spanish, Is.EqualTo("Solo se aceptan archivos PDF (application/pdf)."));
    }

    [Test]
    public async Task SignedUploadService_RejectWithoutComment_ReturnsCode_TranslatedToSpanish()
    {
        var dbName = $"trans-no-comment-{Guid.NewGuid():N}";
        var (appId, _, pendingId) = await SignedUploadEndpointsTestSeeder.SeedPendingUploadAsync(dbName);

        using var ctx = SignedUploadEndpointsTestSeeder.CreateContext(dbName);
        var service = SignedUploadEndpointsTestSeeder.BuildService(ctx);

        var result = await service.RejectAsync(new RejectSignedUploadCommand(
            ApplicationId: appId,
            ReviewerUserId: "admin-1",
            IsAdministrator: true,
            IsReviewerAssigned: false,
            SignedUploadId: pendingId,
            Comment: ""));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code,
            Is.EqualTo(UserFacingErrorCode.SignedUploadRejectionCommentRequired));
        Assert.That(_translator.Translate(result.Error),
            Is.EqualTo("Se requiere un comentario para rechazar la carga."));
    }

    [Test]
    public async Task ReviewService_ReviewItem_OnApplicationNotFound_TranslatesToSpanish()
    {
        // Drive ReviewService directly with no seeded application — it must
        // surface ApplicationNotFound, which the translator renders to es-CR.
        var dbName = $"trans-rev-{Guid.NewGuid():N}";
        using var ctx = SignedUploadEndpointsTestSeeder.CreateContext(dbName);

        var appRepo = new FundingPlatform.Infrastructure.Persistence.Repositories
            .ApplicationRepository(ctx);
        var service = new ReviewService(
            appRepo,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ReviewService>.Instance);

        var error = await service.ReviewItemAsync(
            applicationId: 9999,
            itemId: 1,
            decision: "Approve",
            comment: null,
            selectedSupplierId: 1,
            userId: "reviewer-1");

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo(UserFacingErrorCode.ApplicationNotFound));
        Assert.That(_translator.Translate(error), Is.EqualTo("Solicitud no encontrada."));
    }
}

/// <summary>
/// Shared seeding helpers for the cross-test signed-upload scenarios. Keeps
/// translation tests independent from <see cref="SignedUploadEndpointsTests"/>
/// while reusing its known-good seed graph.
/// </summary>
internal static class SignedUploadEndpointsTestSeeder
{
    public static FundingPlatform.Infrastructure.Persistence.AppDbContext CreateContext(string dbName)
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                FundingPlatform.Infrastructure.Persistence.AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new FundingPlatform.Infrastructure.Persistence.AppDbContext(options);
    }

    public static SignedUploadService BuildService(
        FundingPlatform.Infrastructure.Persistence.AppDbContext ctx)
    {
        var appRepo = new FundingPlatform.Infrastructure.Persistence.Repositories
            .ApplicationRepository(ctx);
        var suRepo = new FundingPlatform.Infrastructure.Persistence.Repositories
            .SignedUploadRepository(ctx);
        var options = Microsoft.Extensions.Options.Options.Create(
            new FundingPlatform.Application.Options.SignedUploadOptions { MaxSizeBytes = 20L * 1024 * 1024 });
        var storage = new InMemoryFileStorage();
        return new SignedUploadService(
            appRepo, suRepo, storage, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SignedUploadService>.Instance);
    }

    private static System.IO.Stream PdfStream()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nfake PDF body");
        return new MemoryStream(bytes);
    }

    public static (FundingPlatform.Domain.Entities.Application Application, string ApplicantUserId)
        SeedAcceptedApplicationWithAgreement(
            FundingPlatform.Infrastructure.Persistence.AppDbContext ctx)
    {
        var uniq = Guid.NewGuid().ToString("N");
        var applicantUserId = $"user-{uniq}";
        var applicant = new FundingPlatform.Domain.Entities.Applicant(
            userId: applicantUserId,
            legalId: "LEG-1",
            firstName: "Ana",
            lastName: "Applicant",
            email: $"ana-{uniq}@example.com",
            phone: null,
            performanceScore: null);
        ctx.Applicants.Add(applicant);
        ctx.SaveChanges();

        var category = new FundingPlatform.Domain.Entities.Category("Equipment", "desc", isActive: true);
        ctx.Categories.Add(category);
        ctx.SaveChanges();

        var application = new FundingPlatform.Domain.Entities.Application(applicant.Id);
        application.AddItem(new FundingPlatform.Domain.Entities.Item("Laptop", category.Id, "specs"));
        typeof(FundingPlatform.Domain.Entities.Application)
            .GetProperty("State")!
            .SetValue(application, FundingPlatform.Domain.Enums.ApplicationState.Resolved);
        ctx.Applications.Add(application);
        ctx.SaveChanges();

        var itemId = application.Items[0].Id;
        application.SubmitResponse(
            new Dictionary<int, FundingPlatform.Domain.Enums.ItemResponseDecision>
            {
                [itemId] = FundingPlatform.Domain.Enums.ItemResponseDecision.Accept
            },
            submittedByUserId: applicant.UserId);

        application.GenerateFundingAgreement(
            "agreement.pdf", "application/pdf", 1024, "/store/agreement.pdf", "admin-1");

        ctx.SaveChanges();
        return (application, applicantUserId);
    }

    public static async Task<(int appId, string applicantUserId, int pendingId)> SeedPendingUploadAsync(string dbName)
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

    private sealed class InMemoryFileStorage : FundingPlatform.Application.Abstractions.Storage.IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public async Task<FundingPlatform.Application.Abstractions.Storage.StoredObject> UploadAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory category,
            FundingPlatform.Application.Abstractions.Storage.ObjectKey key,
            System.IO.Stream content, string contentType, long? contentLength, CancellationToken ct)
        {
            using var ms = new System.IO.MemoryStream();
            await content.CopyToAsync(ms, ct);
            _store[key.Value] = ms.ToArray();
            return new FundingPlatform.Application.Abstractions.Storage.StoredObject(
                Container: key.Container, Key: key.Value, SizeBytes: ms.Length,
                ContentType: contentType, CreatedAt: DateTimeOffset.UtcNow,
                Provider: FundingPlatform.Application.Abstractions.Storage.StorageProviderName.LocalFilesystem);
        }

        public Task<System.IO.Stream> OpenReadAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory category,
            FundingPlatform.Application.Abstractions.Storage.ObjectKey key,
            CancellationToken ct)
        {
            if (!_store.TryGetValue(key.Value, out var bytes))
                throw new FundingPlatform.Application.Abstractions.Storage.ObjectNotFoundException(
                    key.Container, key.Value);
            return Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream(bytes));
        }

        public Task<bool> ExistsAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory category,
            FundingPlatform.Application.Abstractions.Storage.ObjectKey key,
            CancellationToken ct) => Task.FromResult(_store.ContainsKey(key.Value));

        public Task DeleteAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory category,
            FundingPlatform.Application.Abstractions.Storage.ObjectKey key,
            CancellationToken ct)
        {
            _store.Remove(key.Value);
            return Task.CompletedTask;
        }

        public Task<FundingPlatform.Application.Abstractions.Storage.StorageHandle> ResolveServingHandleAsync(
            FundingPlatform.Application.Abstractions.Storage.FileCategory category,
            FundingPlatform.Application.Abstractions.Storage.ObjectKey key,
            FundingPlatform.Application.Abstractions.Storage.ServingMode preferred,
            CancellationToken ct)
        {
            if (!_store.TryGetValue(key.Value, out var bytes))
                throw new FundingPlatform.Application.Abstractions.Storage.ObjectNotFoundException(
                    key.Container, key.Value);
            return Task.FromResult<FundingPlatform.Application.Abstractions.Storage.StorageHandle>(
                new FundingPlatform.Application.Abstractions.Storage.BackendStreamHandle(
                    new System.IO.MemoryStream(bytes), "application/pdf", bytes.Length));
        }
    }
}
