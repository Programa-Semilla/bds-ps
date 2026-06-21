// Spec 021 / US5 / T123 / FR-028 — controller-level coverage proving the
// /Account/ForgotPassword POST returns the *same* neutral response whether
// or not the supplied email is on file. Email-on-file → an envelope is
// captured by the in-memory IEmailSender; unknown email → zero envelopes.
// The response status code, view name, and TempData success banner copy
// MUST match across the two branches (no enumeration).

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Identity;
using FundingPlatform.Application.Notifications.Email;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Email;
using FundingPlatform.Web.Controllers;
using FundingPlatform.Web.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FundingPlatform.Tests.Integration.Identity;

[TestFixture]
public class ForgotPasswordEnumerationTests
{
    private CapturingEmailSender _capture = null!;
    private IIssuePasswordResetTokenHandler _issueHandler = null!;

    [SetUp]
    public void Setup()
    {
        _capture = new CapturingEmailSender();
        // Stubbed issue handler — drives both branches without needing
        // ASP.NET Identity's UserManager (which requires a configured
        // EF Identity store). The handler is the only contributor to the
        // branch decision; everything downstream of the controller's call
        // is identical.
        _issueHandler = Substitute.For<IIssuePasswordResetTokenHandler>();
    }

    [Test]
    public async Task Post_KnownEmail_RendersNeutralResponse_AndSendsEmail()
    {
        _issueHandler.HandleAsync(Arg.Any<IssuePasswordResetTokenCommand>(), Arg.Any<CancellationToken>())
            .Returns(IssuePasswordResetTokenResult.Issued(
                userId: "user-1",
                email: "user@example.com",
                firstName: "Vivi",
                rawToken: "raw-token-xyz"));

        var (controller, tempData) = BuildController();
        var result = await controller.ForgotPassword(
            new ForgotPasswordViewModel { Email = "user@example.com" }, CancellationToken.None);

        AssertNeutralView(result, tempData);
        Assert.That(_capture.Sent, Has.Count.EqualTo(1),
            "Known-email branch MUST dispatch the reset email envelope");
        Assert.That(_capture.Sent[0].ToAddress, Is.EqualTo("user@example.com"));
        Assert.That(_capture.Sent[0].Subject, Is.EqualTo("Restablezca su contraseña"));
    }

    [Test]
    public async Task Post_UnknownEmail_RendersIdenticalNeutralResponse_AndSendsNothing()
    {
        _issueHandler.HandleAsync(Arg.Any<IssuePasswordResetTokenCommand>(), Arg.Any<CancellationToken>())
            .Returns(IssuePasswordResetTokenResult.UnknownUser());

        var (controller, tempData) = BuildController();
        var result = await controller.ForgotPassword(
            new ForgotPasswordViewModel { Email = "unknown@example.com" }, CancellationToken.None);

        AssertNeutralView(result, tempData);
        Assert.That(_capture.Sent, Is.Empty,
            "Unknown-email branch MUST NOT dispatch any email (no enumeration)");
    }

    [Test]
    public async Task BothBranches_ProduceIndistinguishableResponses()
    {
        // Drive both branches back to back; the rendered view + TempData
        // banner copy MUST match (this is the contract callers see).
        _issueHandler.HandleAsync(Arg.Any<IssuePasswordResetTokenCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                IssuePasswordResetTokenResult.Issued("u", "u@e.com", "U", "tok"),
                IssuePasswordResetTokenResult.UnknownUser());

        var (controller, tempData1) = BuildController();
        var known = await controller.ForgotPassword(
            new ForgotPasswordViewModel { Email = "user@example.com" }, CancellationToken.None);

        // Drain captured sends so the second call is clean.
        _capture.Sent.Clear();

        var (controller2, tempData2) = BuildController();
        var unknown = await controller2.ForgotPassword(
            new ForgotPasswordViewModel { Email = "unknown@example.com" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(known, Is.TypeOf<ViewResult>());
            Assert.That(unknown, Is.TypeOf<ViewResult>());
            var knownView = (ViewResult)known;
            var unknownView = (ViewResult)unknown;
            Assert.That(knownView.ViewName, Is.EqualTo(unknownView.ViewName),
                "View name MUST be identical across known + unknown branches");
            Assert.That(tempData1["SuccessMessage"], Is.EqualTo(tempData2["SuccessMessage"]),
                "Neutral banner copy MUST be identical across branches");
        });
    }

    private static void AssertNeutralView(IActionResult result, ITempDataDictionary tempData)
    {
        Assert.That(result, Is.TypeOf<ViewResult>(),
            "Both branches MUST render a ViewResult, not a redirect");
        var view = (ViewResult)result;
        Assert.That(view.ViewName, Is.Null.Or.EqualTo(nameof(AccountController.ForgotPassword)));
        Assert.That(tempData.ContainsKey("SuccessMessage"), Is.True,
            "Neutral banner MUST be set on both branches");
        var banner = tempData["SuccessMessage"] as string;
        Assert.That(banner, Does.Contain("Si la dirección está registrada"),
            "Banner copy MUST be the spec-aligned neutral string");
    }

    private (AccountController, ITempDataDictionary) BuildController()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        var provider = services.BuildServiceProvider();

        // Spec 041 — the factory now renders through IEmailViewRenderer; this suite
        // asserts enumeration-neutral banners, not the email body, so a no-op renderer
        // double suffices (the send is best-effort and its result is ignored here).
        var emailBaseUrl = new StubBaseUrlProvider("https://localhost");
        var factory = new ForgotPasswordEmailFactory(
            new NoopEmailViewRenderer(),
            emailBaseUrl,
            NullLogger<ForgotPasswordEmailFactory>.Instance);

        // We construct the controller with the stub Identity-flow handlers
        // we need. Real UserManager/SignInManager are NOT exercised — they
        // are required by the AccountController constructor signature but
        // none of the actions under test touch them.
        var userManager = IdentityTestDoubles.UserManager();
        var signInManager = IdentityTestDoubles.SignInManager(userManager);

        var dbCtx = TestAppDbContextFactory.Create();
        var controller = new AccountController(
            userManager,
            signInManager,
            dbCtx,
            new TestWebHostEnvironment(),
            _issueHandler,
            Substitute.For<IConsumePasswordResetTokenHandler>(),
            Substitute.For<IUpdateProfileHandler>(),
            _capture,
            factory,
            new PasswordChangedEmailFactory(
                new NoopEmailViewRenderer(), emailBaseUrl,
                NullLogger<PasswordChangedEmailFactory>.Instance));

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
        var tempData = new TempDataDictionary(httpContext,
            Substitute.For<ITempDataProvider>());
        controller.ControllerContext = new ControllerContext(actionContext);
        controller.TempData = tempData;

        // Url.Action requires an IUrlHelper.
        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.Action(Arg.Any<UrlActionContext>())
            .Returns(ci => $"https://localhost/Account/ResetPassword?userId=u&token=t");
        controller.Url = urlHelper;

        return (controller, tempData);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "FundingPlatform.Tests.Integration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "FundingPlatform.Tests.Integration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>Spec 041 — no-op email view renderer (this suite never inspects the body).</summary>
    private sealed class NoopEmailViewRenderer : IEmailViewRenderer
    {
        public Task<string> RenderViewAsync(string viewPath, object model, bool disableLayout, CancellationToken ct)
            => Task.FromResult(string.Empty);
    }

    private sealed class StubBaseUrlProvider(string baseUrl) : IEmailBaseUrlProvider
    {
        public string GetBaseUrl() => baseUrl;
    }
}
