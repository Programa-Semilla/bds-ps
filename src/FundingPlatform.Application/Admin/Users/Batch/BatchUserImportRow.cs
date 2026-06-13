namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — one parsed CSV data row (raw cells, untrimmed). The controller maps
/// parsed columns into this transient type; the service validates/normalizes it.
/// <see cref="RowNumber"/> is the 1-based data-row number (header excluded), shown
/// in the report.
/// </summary>
public sealed record BatchUserImportRow(
    int RowNumber,
    string Grupo,
    string Proceso,
    string Fondo,
    string Nombre,
    string Apellido1,
    string Apellido2,
    string Email,
    string Telefono,
    string Cedula,
    string CodigoUsuario);
