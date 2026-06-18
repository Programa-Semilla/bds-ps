// Spec 021 — see specs/021-feedback-session-may13/spec.md FR-007 + US3 +
// contracts/admin-routes.md (SupplierAdmin scope section).

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FundingPlatform.Web.Filters;

/// <summary>
/// Spec 021 / US3 / T105 / FR-007 — authorization filter applied to the
/// <c>/Admin/Suppliers*</c> surface. Allows users that hold either the
/// <c>Admin</c> role (existing platform admin) OR the new <c>SupplierAdmin</c>
/// role to pass; any other authenticated user is short-circuited with HTTP 403
/// and the Tabler-styled <c>Error403</c> view.
///
/// <para>
/// This attribute is the affirmative counterpart of
/// <see cref="SupplierAdminDeniedAttribute"/>. The two together implement the
/// FR-007 matrix:
/// </para>
///
/// <list type="bullet">
///   <item>SupplierAdmin user × <c>/Admin/Suppliers*</c> → allow (this filter).</item>
///   <item>SupplierAdmin user × every other <c>/Admin/*</c> → deny + audit
///         (<see cref="SupplierAdminDeniedAttribute"/>).</item>
///   <item>Admin user × everywhere → allow (both filters short-circuit on Admin role).</item>
/// </list>
///
/// <para>
/// Class-level usage on the controller (mirrors the existing
/// <c>[Authorize(Roles = "Admin")]</c> pattern). Note: the codebase normalizes
/// the platform-admin role name as <c>"Admin"</c> (not <c>"Administrator"</c>);
/// see <c>AdminController.cs</c> + <c>AccountController.AssignRole</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SupplierAdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Platform-admin role discriminator (existing codebase convention).</summary>
    public const string AdminRole = "Admin";

    /// <summary>Spec 021 / FR-007 — supplier-admin role discriminator.</summary>
    public const string SupplierAdminRole = "Auditor";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            // Unauthenticated callers fall through to the framework's
            // [Authorize] redirect path so the login UX stays consistent.
            context.Result = new ChallengeResult();
            return;
        }

        if (user.IsInRole(AdminRole) || user.IsInRole(SupplierAdminRole))
        {
            return;
        }

        context.Result = new ViewResult
        {
            ViewName = "Error403",
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
