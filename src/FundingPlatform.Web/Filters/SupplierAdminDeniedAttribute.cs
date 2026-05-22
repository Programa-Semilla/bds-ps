// Spec 021 — see specs/021-feedback-session-may13/spec.md FR-007 + US3 +
// contracts/admin-routes.md (Denied surfaces section) +
// contracts/audit-events.md (SupplierAdminDeniedAccess payload).

using System.Security.Claims;
using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FundingPlatform.Web.Filters;

/// <summary>
/// Spec 021 / US3 / T106 / FR-007 — class-level filter applied to every admin
/// controller OTHER than <c>AdminSuppliersController</c>. When the caller holds
/// ONLY the <c>SupplierAdmin</c> role (and NOT also <c>Admin</c>), the filter:
///
/// <list type="number">
///   <item>Writes an <c>AdminAuditEvent</c> row with kind
///         <c>supplier_admin.denied_access</c> via the spec-021
///         <see cref="IAdminAuditEventWriter"/> seam, with payload
///         <c>{ route, method, userAgent }</c> per
///         <c>contracts/audit-events.md</c>.</item>
///   <item>Saves the audit row in its own short transaction (we are short-
///         circuiting the action, so the controller's UnitOfWork will not run).</item>
///   <item>Returns HTTP 403 with the Tabler-styled <c>Error403</c> view.</item>
/// </list>
///
/// <para>
/// Admin users pass through (no audit row). Unauthenticated callers fall
/// through to the existing <c>[Authorize(Roles = "Admin")]</c> on each
/// controller — the framework issues a challenge / login redirect.
/// </para>
///
/// <para>
/// The filter implements <see cref="IAsyncAuthorizationFilter"/> so it runs
/// BEFORE any model binding or the action body, and uses
/// <see cref="IFilterFactory"/> so DI can supply
/// <see cref="IAdminAuditEventWriter"/> + the EF context + the user manager.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SupplierAdminDeniedAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => new SupplierAdminDeniedFilter(
            serviceProvider.GetRequiredService<IAdminAuditEventWriter>(),
            serviceProvider.GetRequiredService<AppDbContext>());
}

public sealed class SupplierAdminDeniedFilter : IAsyncAuthorizationFilter
{
    private readonly IAdminAuditEventWriter _audit;
    private readonly AppDbContext _db;

    public SupplierAdminDeniedFilter(
        IAdminAuditEventWriter audit,
        AppDbContext db)
    {
        _audit = audit;
        _db = db;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // Unauthenticated: let the framework's challenge / [Authorize] handle it.
        if (user?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        // Admin users always pass — even if they also happen to hold SupplierAdmin.
        if (user.IsInRole(SupplierAdminOnlyAttribute.AdminRole))
        {
            return;
        }

        // SupplierAdmin-ONLY user reaching a denied surface: write the audit
        // row then return 403 + Error403 view.
        if (user.IsInRole(SupplierAdminOnlyAttribute.SupplierAdminRole))
        {
            // Identity wires NameIdentifier to AspNetUsers.Id by default — same
            // value UserManager<ApplicationUser>.GetUserId(user) returns.
            // Using the claim directly keeps the filter testable without DI.
            var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    route = context.HttpContext.Request.Path.Value ?? string.Empty,
                    method = context.HttpContext.Request.Method,
                    userAgent = (string?)context.HttpContext.Request.Headers.UserAgent.ToString(),
                });

                await _audit.WriteAsync(
                    AdminAuditEvent.SupplierAdminDeniedAccess,
                    actorId,
                    payload,
                    context.HttpContext.RequestAborted);
                // The audit writer stages the row only; we commit here because
                // the action will not run (no UnitOfWork) and we want the
                // attempt recorded regardless. Wrap in try/catch so an audit
                // failure does not change the user-facing 403 outcome.
                try
                {
                    await _db.SaveChangesAsync(context.HttpContext.RequestAborted);
                }
                catch
                {
                    // Audit best-effort; never block the deny response.
                }
            }

            context.Result = new ViewResult
            {
                ViewName = "Error403",
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        // Any other authenticated, non-Admin, non-SupplierAdmin user is
        // already caught by the controller's [Authorize(Roles="Admin")] and
        // would be challenged or denied there. We let it fall through.
    }
}
