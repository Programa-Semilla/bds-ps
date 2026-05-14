// Spec 021 / T089 / R-8 — POM helper that crawls a list of routes and asserts
// rendered HTML on each contains zero matches for a configured set of regex
// patterns. Used by US2 (PublicCode sweep — `Solicitud N.º \d+` forbidden)
// and US7 (financiamiento / Bienvenido sweep).

using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace FundingPlatform.Tests.E2E.PageObjects;

/// <summary>
/// Spec 021 / T089 — single helper used by both US2 (`Solicitud N.º \d+`
/// sweep) and US7 (financiamiento / Bienvenido sweep) per R-8.
/// </summary>
public sealed class ForbiddenStringsCrawler
{
    private readonly IPage _page;
    private readonly string _baseUrl;
    private readonly IReadOnlyList<string> _routes;

    public ForbiddenStringsCrawler(IPage page, string baseUrl, IEnumerable<string> routes)
    {
        _page = page;
        _baseUrl = baseUrl.TrimEnd('/');
        _routes = routes.ToList();
    }

    /// <summary>
    /// Opens each configured route in turn and asserts the rendered HTML
    /// contains zero matches for every supplied regex. On failure, surfaces
    /// the first matching snippet (route + match) so the diagnostic message
    /// names the offending surface rather than a generic "regex matched".
    /// </summary>
    public async Task AssertNoMatchesAsync(IEnumerable<Regex> patterns)
    {
        var compiled = patterns.ToList();
        foreach (var route in _routes)
        {
            var url = route.StartsWith("http")
                ? route
                : $"{_baseUrl}/{route.TrimStart('/')}";
            await _page.GotoAsync(url);
            var html = await _page.ContentAsync();
            foreach (var pattern in compiled)
            {
                var match = pattern.Match(html);
                if (match.Success)
                {
                    // Pull a 120-char window around the match so the failure
                    // message points at the offending fragment.
                    var start = Math.Max(0, match.Index - 60);
                    var len = Math.Min(120, html.Length - start);
                    var snippet = html.Substring(start, len);
                    Assert.Fail(
                        $"Forbidden string '{pattern}' matched on '{route}' "
                        + $"at offset {match.Index}: ...{snippet}...");
                }
            }
        }
    }
}
