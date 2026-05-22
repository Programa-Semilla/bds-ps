using FundingPlatform.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FundingPlatform.Web.Filters;

/// <summary>
/// Spec 021 / R-13 — global exception filter that maps domain "window-closed"
/// exceptions to HTTP 422 with an es-CR <see cref="ProblemDetails"/> payload.
///
/// <list type="bullet">
///   <item><see cref="StageWindowClosedException"/> → 422 ("Etapa cerrada").</item>
///   <item><see cref="ProcessClosedException"/> → 422 ("Proceso cerrado").</item>
/// </list>
///
/// All other exceptions are left untouched so the framework's default
/// exception handler / developer page can take over.
///
/// Registered globally in <c>Program.cs</c> via
/// <c>AddControllersWithViews(o => o.Filters.Add&lt;DomainExceptionFilter&gt;())</c>.
/// </summary>
public sealed class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case StageWindowClosedException stageEx:
                context.Result = new UnprocessableEntityObjectResult(new ProblemDetails
                {
                    Title = "Etapa cerrada",
                    Detail = stageEx.Message,
                    Status = StatusCodes.Status422UnprocessableEntity,
                });
                context.ExceptionHandled = true;
                break;

            case ProcessClosedException processEx:
                context.Result = new UnprocessableEntityObjectResult(new ProblemDetails
                {
                    Title = "Proceso cerrado",
                    Detail = processEx.Message,
                    Status = StatusCodes.Status422UnprocessableEntity,
                });
                context.ExceptionHandled = true;
                break;

            // Default: leave the exception unhandled so other middleware (e.g.
            // the developer exception page or UseExceptionHandler) can bubble it.
            default:
                break;
        }
    }
}
