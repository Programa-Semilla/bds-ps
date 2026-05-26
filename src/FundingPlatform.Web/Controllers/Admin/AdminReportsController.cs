using System.Globalization;
using System.Net.Mime;
using System.Text;
using FundingPlatform.Application.Admin.Reports;
using FundingPlatform.Application.Admin.Reports.DTOs;
using FundingPlatform.Application.Admin.Reports.Services;
using FundingPlatform.Application.Exceptions;
using FundingPlatform.Web.Filters;
using FundingPlatform.Web.Helpers;
using FundingPlatform.Web.ViewModels.Admin.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingPlatform.Web.Controllers.Admin;

[Authorize(Roles = "Admin,SupplierAdmin")]
[SupplierAdminDenied]
[Route("Admin/Reports")]
public class AdminReportsController : Controller
{
    private readonly IAdminReportsService _reportsService;

    public AdminReportsController(IAdminReportsService reportsService)
    {
        _reportsService = reportsService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var range = new DateRange(from ?? today.AddDays(-30), to ?? today);
        var result = await _reportsService.GetDashboardAsync(range, ct);

        // es-CR: render the pipeline state as its Spanish label (the same
        // StatusVisualMap source the _StatusPill uses) instead of the English
        // enum name (p.State.ToString()) that leaked onto the dashboard.
        var pipelineTiles = result.Pipeline
            .Select(p => new KpiTileViewModel(StatusVisualMap.For(p.State).DisplayLabel, p.Count, null))
            .ToList();

        var financialTiles = result.Financial
            .Select(f => new KpiTileViewModel(f.Label, null, f.Stack))
            .ToList();

        var applicantTiles = result.Applicants
            .Select(a => new KpiTileViewModel(a.Label, a.Count, null))
            .ToList();

        var vm = new DashboardViewModel
        {
            AppliedRange = result.AppliedRange,
            PipelineTiles = pipelineTiles,
            FinancialTiles = financialTiles,
            ApplicantTiles = applicantTiles,
        };

        return View(vm);
    }

    [HttpGet("Applications")]
    public async Task<IActionResult> Applications([FromQuery] ListApplicationsRequest req, CancellationToken ct)
    {
        // Force the fixed page size; clients cannot override.
        req.PageSize = AdminReportsService.PageSize;
        var result = await _reportsService.ListApplicationsAsync(req, ct);

        var totalPages = (int)Math.Ceiling((double)result.TotalCount / AdminReportsService.PageSize);
        var vm = new ApplicationsViewModel
        {
            Result = result,
            PageSize = AdminReportsService.PageSize,
            CurrentPage = Math.Max(1, req.Page),
            TotalPages = Math.Max(1, totalPages),
        };

        return View(vm);
    }

    [HttpGet("Applicants")]
    public async Task<IActionResult> Applicants([FromQuery] ListApplicantsRequest req, CancellationToken ct)
    {
        req.PageSize = AdminReportsService.PageSize;
        var result = await _reportsService.ListApplicantsAsync(req, ct);
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / AdminReportsService.PageSize);
        var vm = new ApplicantsViewModel
        {
            Result = result,
            PageSize = AdminReportsService.PageSize,
            CurrentPage = Math.Max(1, req.Page),
            TotalPages = Math.Max(1, totalPages),
        };
        return View(vm);
    }

    [HttpGet("Applicants/Export")]
    public IActionResult ExportApplicants([FromQuery] ListApplicantsRequest req, CancellationToken ct)
    {
        try
        {
            var enumerator = _reportsService.ExportApplicantsCsvAsync(req, ct);
            return new CsvFileStreamResult(enumerator, "applicants.csv");
        }
        catch (CsvRowBoundExceededException ex)
        {
            return BadRequest(new
            {
                error = "CsvRowBoundExceeded",
                limit = ex.Limit,
                actualCount = ex.ActualCount,
                hint = "Narrow your filter and try again."
            });
        }
    }

    [HttpGet("FundedItems")]
    public async Task<IActionResult> FundedItems([FromQuery] ListFundedItemsRequest req, CancellationToken ct)
    {
        req.PageSize = AdminReportsService.PageSize;
        var result = await _reportsService.ListFundedItemsAsync(req, ct);
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / AdminReportsService.PageSize);
        var vm = new FundedItemsViewModel
        {
            Result = result,
            PageSize = AdminReportsService.PageSize,
            CurrentPage = Math.Max(1, req.Page),
            TotalPages = Math.Max(1, totalPages),
        };
        return View(vm);
    }

    [HttpGet("FundedItems/Export")]
    public IActionResult ExportFundedItems([FromQuery] ListFundedItemsRequest req, CancellationToken ct)
    {
        try
        {
            var enumerator = _reportsService.ExportFundedItemsCsvAsync(req, ct);
            return new CsvFileStreamResult(enumerator, "funded-items.csv");
        }
        catch (CsvRowBoundExceededException ex)
        {
            return BadRequest(new
            {
                error = "CsvRowBoundExceeded",
                limit = ex.Limit,
                actualCount = ex.ActualCount,
                hint = "Narrow your filter and try again."
            });
        }
    }

    [HttpGet("Aging")]
    public async Task<IActionResult> Aging([FromQuery] ListAgingApplicationsRequest req, CancellationToken ct)
    {
        req.PageSize = AdminReportsService.PageSize;
        try
        {
            var result = await _reportsService.ListAgingApplicationsAsync(req, ct);
            var totalPages = (int)Math.Ceiling((double)result.TotalCount / AdminReportsService.PageSize);
            var vm = new AgingApplicationsViewModel
            {
                Result = result,
                PageSize = AdminReportsService.PageSize,
                CurrentPage = Math.Max(1, req.Page),
                TotalPages = Math.Max(1, totalPages),
            };
            return View(vm);
        }
        catch (ArgumentOutOfRangeException)
        {
            ViewData["ReportFilterError"] = "El umbral (días) debe estar entre 1 y 365 inclusive.";
            req.Threshold = 14;
            var result = await _reportsService.ListAgingApplicationsAsync(req, ct);
            var totalPages = (int)Math.Ceiling((double)result.TotalCount / AdminReportsService.PageSize);
            var vm = new AgingApplicationsViewModel
            {
                Result = result,
                PageSize = AdminReportsService.PageSize,
                CurrentPage = Math.Max(1, req.Page),
                TotalPages = Math.Max(1, totalPages),
            };
            return View(vm);
        }
    }

    [HttpGet("Aging/Export")]
    public IActionResult ExportAging([FromQuery] ListAgingApplicationsRequest req, CancellationToken ct)
    {
        try
        {
            var enumerator = _reportsService.ExportAgingApplicationsCsvAsync(req, ct);
            return new CsvFileStreamResult(enumerator, "aging-applications.csv");
        }
        catch (CsvRowBoundExceededException ex)
        {
            return BadRequest(new
            {
                error = "CsvRowBoundExceeded",
                limit = ex.Limit,
                actualCount = ex.ActualCount,
                hint = "Narrow your filter and try again."
            });
        }
    }

    [HttpGet("Applications/Export")]
    public async Task<IActionResult> ExportApplications([FromQuery] ListApplicationsRequest req, CancellationToken ct)
    {
        try
        {
            // Materialize lazily into the response stream.
            var enumerator = _reportsService.ExportApplicationsCsvAsync(req, ct);
            return new CsvFileStreamResult(enumerator, "applications.csv");
        }
        catch (CsvRowBoundExceededException ex)
        {
            return BadRequest(new
            {
                error = "CsvRowBoundExceeded",
                limit = ex.Limit,
                actualCount = ex.ActualCount,
                hint = "Narrow your filter and try again."
            });
        }
    }
}

internal sealed class CsvFileStreamResult : IActionResult
{
    private readonly IAsyncEnumerable<string> _lines;
    private readonly string _fileName;

    public CsvFileStreamResult(IAsyncEnumerable<string> lines, string fileName)
    {
        _lines = lines;
        _fileName = fileName;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var ct = context.HttpContext.RequestAborted;
        var response = context.HttpContext.Response;
        response.ContentType = "text/csv; charset=utf-8";
        response.Headers["Content-Disposition"] = $"attachment; filename={_fileName}";

        // UTF-8 BOM so Excel auto-detects encoding.
        await response.Body.WriteAsync(new byte[] { 0xEF, 0xBB, 0xBF }, ct);

        try
        {
            await foreach (var line in _lines.WithCancellation(ct))
            {
                var bytes = Encoding.UTF8.GetBytes(line);
                await response.Body.WriteAsync(bytes, ct);
            }
        }
        catch (CsvRowBoundExceededException ex)
        {
            // The bound is enforced before any rows stream, so we can still set the
            // status code and JSON body cleanly here.
            if (!response.HasStarted)
            {
                response.Clear();
                response.StatusCode = StatusCodes.Status400BadRequest;
                response.ContentType = MediaTypeNames.Application.Json;
                var payload = $"{{\"error\":\"CsvRowBoundExceeded\",\"limit\":{ex.Limit.ToString(CultureInfo.InvariantCulture)},\"actualCount\":{ex.ActualCount.ToString(CultureInfo.InvariantCulture)},\"hint\":\"Narrow your filter and try again.\"}}";
                await response.WriteAsync(payload, ct);
                return;
            }
            throw;
        }
    }
}
