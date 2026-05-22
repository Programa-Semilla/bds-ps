// Spec 021 / US5 / T123 — minimal Identity test doubles used by the
// no-enumeration controller-level test. The AccountController constructor
// requires concrete UserManager/SignInManager, but the actions exercised
// by ForgotPasswordEnumerationTests do not call any UserManager method:
// the issue-token handler is stubbed (NSubstitute) and the email factory
// is the only collaborator that runs end-to-end.

using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FundingPlatform.Tests.Integration.Identity;

internal static class IdentityTestDoubles
{
    public static UserManager<ApplicationUser> UserManager()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        return new UserManager<ApplicationUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    public static SignInManager<ApplicationUser> SignInManager(UserManager<ApplicationUser> userManager)
    {
        var contextAccessor = Substitute.For<IHttpContextAccessor>();
        contextAccessor.HttpContext.Returns(new DefaultHttpContext());
        var claimsFactory = Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return new SignInManager<ApplicationUser>(
            userManager,
            contextAccessor,
            claimsFactory,
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<ApplicationUser>>());
    }
}

internal static class TestAppDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"forgot-enum-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }
}
