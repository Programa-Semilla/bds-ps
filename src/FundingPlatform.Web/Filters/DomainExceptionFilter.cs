using FundingPlatform.Application.Errors;
using FundingPlatform.Application.Time;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.ReceptionWindows;
using FundingPlatform.Web.Localization;
using FundingPlatform.Web.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FundingPlatform.Web.Filters;

/// <summary>
/// Spec 021 / R-13 — global exception filter that maps domain "window-closed"
/// exceptions to HTTP 422 with an es-CR <see cref="ProblemDetails"/> payload.
///
/// <list type="bullet">
///   <item><see cref="ReceptionWindowClosedException"/> → 422 ("Recepción cerrada",
///   spec 044 — typed es-CR message with the open/close instant in CR time).</item>
///   <item><see cref="StageWindowClosedException"/> → 422 ("Etapa cerrada") — retained
///   for any non-Solicitud usage of the legacy stage window.</item>
///   <item><see cref="ProcessClosedException"/> → 422 ("Proceso cerrado").</item>
/// </list>
///
/// Registered globally in <c>Program.cs</c> via
/// <c>AddControllersWithViews(o => o.Filters.Add&lt;DomainExceptionFilter&gt;())</c>
/// (a type filter — constructor dependencies are resolved from DI).
/// </summary>
public sealed class DomainExceptionFilter : IExceptionFilter
{
    private readonly IBusinessTimeZone _businessTime;
    private readonly IUserFacingErrorTranslator _translator;

    public DomainExceptionFilter(IBusinessTimeZone businessTime, IUserFacingErrorTranslator translator)
    {
        _businessTime = businessTime;
        _translator = translator;
    }

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            // Spec 044 / FR-008 — reception window not open.
            case ReceptionWindowClosedException receptionEx:
                context.Result = new UnprocessableEntityObjectResult(new ProblemDetails
                {
                    Title = ReceptionWindowResources.RefusalTitle,
                    Detail = _translator.Translate(
                        UserFacingError.From(UserFacingErrorCode.ReceptionWindowClosed,
                            BuildReceptionMessage(receptionEx))),
                    Status = StatusCodes.Status422UnprocessableEntity,
                });
                context.ExceptionHandled = true;
                break;

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

    private string BuildReceptionMessage(ReceptionWindowClosedException ex)
    {
        if (ex.BoundaryUtc is not { } boundaryUtc)
        {
            return ReceptionWindowResources.RefusalGeneric;
        }

        var instant = _businessTime.ToBusinessLocal(boundaryUtc).ToString("dd/MM/yyyy HH:mm");
        return ex.Status == SubmissionAvailabilityStatus.AllWindowsClosed
            ? ReceptionWindowResources.RefusalAllClosed(instant)
            : ReceptionWindowResources.RefusalBeforeOpen(instant);
    }
}
