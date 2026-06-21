using FundingPlatform.Application.Abstractions.Hacienda;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Hacienda;

namespace FundingPlatform.Tests.Unit.Infrastructure;

/// <summary>
/// Spec 043 / research D1 — exhaustive mapping table for <see cref="HaciendaStatusMapper"/>.
/// One case per row plus the unrecognized-estado → failure (null) case.
/// </summary>
[TestFixture]
public class HaciendaStatusMapperTests
{
    private static HaciendaLookupResult Found(string estado, bool moroso, bool omiso)
        => HaciendaLookupResult.Found(null, new HaciendaSituacion(estado, moroso, omiso));

    [Test]
    public void Inscrito_NoMoroso_NoOmiso_AlDia()
        => Assert.That(HaciendaStatusMapper.Map(Found("Inscrito", false, false)), Is.EqualTo(HaciendaStatus.AlDia));

    [Test]
    public void Inscrito_Moroso_EstadoMoroso_RegardlessOfOmiso()
    {
        Assert.That(HaciendaStatusMapper.Map(Found("Inscrito", true, false)), Is.EqualTo(HaciendaStatus.EstadoMoroso));
        Assert.That(HaciendaStatusMapper.Map(Found("Inscrito", true, true)), Is.EqualTo(HaciendaStatus.EstadoMoroso));
    }

    [Test]
    public void Inscrito_NoMoroso_Omiso_CobroAdministrativo()
        => Assert.That(HaciendaStatusMapper.Map(Found("Inscrito", false, true)), Is.EqualTo(HaciendaStatus.CobroAdministrativo));

    [Test]
    public void Desinscrito_NoMoroso_DesinscritoAlDia()
        => Assert.That(HaciendaStatusMapper.Map(Found("Desinscrito", false, false)), Is.EqualTo(HaciendaStatus.DesinscritoAlDia));

    [Test]
    public void Desinscrito_Moroso_DesinscritoMoroso()
        => Assert.That(HaciendaStatusMapper.Map(Found("Desinscrito", true, false)), Is.EqualTo(HaciendaStatus.DesinscritoMoroso));

    [Test]
    public void NoInscrito_SinInscripcion()
        => Assert.That(HaciendaStatusMapper.Map(Found("No inscrito", false, false)), Is.EqualTo(HaciendaStatus.SinInscripcion));

    [Test]
    public void NotRegistered404_SinInformacion()
        => Assert.That(HaciendaStatusMapper.Map(HaciendaLookupResult.NotRegistered()), Is.EqualTo(HaciendaStatus.SinInformacion));

    [Test]
    public void Failed_MapsToNull()
        => Assert.That(HaciendaStatusMapper.Map(HaciendaLookupResult.Failed("boom")), Is.Null);

    [Test]
    public void UnrecognizedEstado_MapsToNull()
        => Assert.That(HaciendaStatusMapper.Map(Found("Algo Raro", false, false)), Is.Null);

    [Test]
    public void EstadoIsCaseInsensitive()
        => Assert.That(HaciendaStatusMapper.Map(Found("INSCRITO", false, false)), Is.EqualTo(HaciendaStatus.AlDia));

    [Test]
    public void FoundWithNullSituacion_MapsToNull()
        => Assert.That(HaciendaStatusMapper.Map(new HaciendaLookupResult(HaciendaLookupKind.Found)), Is.Null);
}
