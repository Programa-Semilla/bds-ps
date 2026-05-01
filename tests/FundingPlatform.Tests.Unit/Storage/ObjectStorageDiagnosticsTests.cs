using FundingPlatform.Application.Abstractions.Storage;
using FundingPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Tests.Unit.Storage;

[TestFixture]
public class ObjectStorageDiagnosticsTests
{
    private static ObjectKey SampleKey() => ObjectKey.Build(
        FileCategory.SignedFundingAgreement,
        "applicants/abc",
        "entity",
        "suffix",
        ".pdf");

    [Test]
    public async Task Emits_single_log_entry_on_success_with_required_fields()
    {
        var logger = new RecordingLogger();
        var diagnostics = new ObjectStorageDiagnostics(logger);

        var result = await diagnostics.TrackAsync<int>(
            "Upload",
            FileCategory.SignedFundingAgreement,
            SampleKey(),
            StorageProviderName.Azurite,
            ctx =>
            {
                ctx.SizeBytes = 4321;
                return Task.FromResult(42);
            });

        Assert.That(result, Is.EqualTo(42));
        Assert.That(logger.Records, Has.Count.EqualTo(1));
        var record = logger.Records[0];
        Assert.That(record.Message, Does.Contain("ObjectStorage.Upload"));
        Assert.That(record.Message, Does.Contain("outcome=Success"));
        Assert.That(record.Message, Does.Contain("provider=Azurite"));
        Assert.That(record.Message, Does.Contain("sizeBytes=4321"));
        Assert.That(record.Message, Does.Contain("durationMs="));
        Assert.That(record.Message, Does.Contain("container=signed-funding-agreements"));
    }

    [Test]
    public void Tags_retry_exhausted_outcome()
    {
        var logger = new RecordingLogger();
        var diagnostics = new ObjectStorageDiagnostics(logger);

        Assert.ThrowsAsync<ObjectStorageOperationException>(async () =>
        {
            await diagnostics.TrackAsync<int>(
                "Upload",
                FileCategory.SignedFundingAgreement,
                SampleKey(),
                StorageProviderName.AzureBlob,
                ctx => throw new ObjectStorageOperationException(
                    ObjectStorageOperationReason.RetryExhausted,
                    "boom"));
        });

        Assert.That(logger.Records, Has.Count.EqualTo(1));
        Assert.That(logger.Records[0].Message, Does.Contain("outcome=RetryExhausted"));
    }

    [Test]
    public void Tags_not_found_outcome()
    {
        var logger = new RecordingLogger();
        var diagnostics = new ObjectStorageDiagnostics(logger);

        Assert.ThrowsAsync<ObjectNotFoundException>(async () =>
        {
            await diagnostics.TrackAsync<int>(
                "Download",
                FileCategory.SignedFundingAgreement,
                SampleKey(),
                StorageProviderName.AzureBlob,
                ctx => throw new ObjectNotFoundException("c", "k"));
        });

        Assert.That(logger.Records[0].Message, Does.Contain("outcome=NotFound"));
    }

    [Test]
    public async Task Does_not_log_blob_contents()
    {
        var logger = new RecordingLogger();
        var diagnostics = new ObjectStorageDiagnostics(logger);
        var sensitive = "TOP_SECRET_PAYLOAD_BYTES";

        await diagnostics.TrackAsync<int>(
            "Upload",
            FileCategory.SignedFundingAgreement,
            SampleKey(),
            StorageProviderName.Azurite,
            ctx => Task.FromResult(sensitive.Length));

        foreach (var record in logger.Records)
            Assert.That(record.Message, Does.Not.Contain(sensitive));
    }

    private sealed class RecordingLogger : ILogger<ObjectStorageDiagnostics>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Records.Add((logLevel, formatter(state, exception)));
        }
    }
}
