using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using FundingPlatform.Application.Suppliers.Services;
using FundingPlatform.Web.Controllers.Admin;
using NUnit.Framework;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 013 NFR-001 verification (T091).
///
/// The supplier catalog is intentionally an internal-only data store. There must
/// be NO outbound HTTP calls to CCSS, Hacienda, SICOP, or any other external
/// service from <see cref="SupplierCatalogService"/> or
/// <see cref="AdminSuppliersController"/>. Compliance flags are admin-curated, not
/// integrated.
///
/// This test fails if either type ever takes a dependency (constructor parameter
/// OR field/property) on:
///   - System.Net.Http.HttpClient
///   - Microsoft.Extensions.Http.IHttpClientFactory (matched by full type name to
///     avoid pulling the package in this unit-test project)
///   - any other System.Net.Http.* type
///
/// If a future PR genuinely needs an external integration it must (a) update the
/// spec, (b) drop this test with a justification, and (c) add new NFR coverage.
/// </summary>
public class SupplierCatalogService_NoExternalCallsTests
{
    private static readonly string[] ForbiddenFullNamePrefixes =
    [
        "System.Net.Http.",
        "Microsoft.Extensions.Http.",
    ];

    [TestCase(typeof(SupplierCatalogService))]
    [TestCase(typeof(AdminSuppliersController))]
    public void Type_ShouldNotDependOnHttpClientOrFactory(Type subject)
    {
        var offending = new List<string>();

        // Constructor parameters.
        foreach (var ctor in subject.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var param in ctor.GetParameters())
            {
                if (IsForbidden(param.ParameterType))
                {
                    offending.Add($"ctor parameter '{param.Name}' : {param.ParameterType.FullName}");
                }
            }
        }

        // Fields (incl. private).
        foreach (var field in subject.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsForbidden(field.FieldType))
            {
                offending.Add($"field '{field.Name}' : {field.FieldType.FullName}");
            }
        }

        // Properties (incl. private).
        foreach (var prop in subject.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (IsForbidden(prop.PropertyType))
            {
                offending.Add($"property '{prop.Name}' : {prop.PropertyType.FullName}");
            }
        }

        Assert.That(offending, Is.Empty,
            $"NFR-001 violation: {subject.FullName} declares dependencies on outbound-network types:\n  - "
            + string.Join("\n  - ", offending));
    }

    [Test]
    public void SupplierCatalogService_AssemblyClosure_DoesNotReferenceHttpClient()
    {
        // Assembly-level smoke check: the Application project (where the catalog
        // lives) MUST NOT reference System.Net.Http directly. If a future PR adds
        // an HttpClient field somewhere else in the Application layer, this guard
        // surfaces it instantly.
        var asm = typeof(SupplierCatalogService).Assembly;
        var referenced = asm.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .ToArray();

        // System.Net.Http is the actual managed assembly that contains HttpClient.
        Assert.That(referenced, Does.Not.Contain("System.Net.Http"),
            "NFR-001: Application assembly MUST NOT reference System.Net.Http. " +
            "Adding outbound HTTP from the application layer requires a spec change.");
    }

    private static bool IsForbidden(Type t)
    {
        if (t == typeof(HttpClient)) return true;
        var fullName = t.FullName ?? string.Empty;
        return ForbiddenFullNamePrefixes.Any(prefix => fullName.StartsWith(prefix, StringComparison.Ordinal));
    }
}
