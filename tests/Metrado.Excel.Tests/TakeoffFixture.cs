using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// Shared setup for the workbook tests.
/// </summary>
/// <remarks>
/// Every scenario varies only the handful of facts the workbook is built from —
/// capitulo, resolved code, identity and the measured amount — while the rest of
/// the takeoff is noise the writer never reads. Building the line in one place
/// keeps the fact under test visible instead of buried under arguments that never
/// change.
/// </remarks>
internal static class TakeoffFixture
{
    /// <summary>One measured instance, ready to be grouped and written.</summary>
    /// <param name="partidaCode">
    /// The code the codification chain resolved. It is carried on the line rather
    /// than read back off <see cref="ElementTakeoff.Codes"/>, because the chain's
    /// answer is what the budget is grouped by.
    /// </param>
    internal static Linea Line(
        string uniqueId,
        string partidaCode,
        double metrado = 10.0,
        string capitulo = "Walls",
        string family = "Basic Wall",
        string typeName = "Generic - 200mm",
        BoundaryMode appliedMode = BoundaryMode.Exclusive,
        QuantityUnit unit = QuantityUnit.SquareMetre)
    {
        ElementTakeoff element = new(
            UniqueId: uniqueId,
            CategoryName: capitulo,
            FamilyName: family,
            TypeName: typeName,
            TypeKey: $"{family}:{typeName}",
            Codes: new CodificationReadings(
                assemblyCode: partidaCode == UnclassifiedResolver.Code ? null : partidaCode,
                keynote: null,
                sharedParameters: new Dictionary<string, string?>()),
            Quantities: [],
            Openings: []);

        Quantity quantity = new(metrado, unit);

        return new Linea(
            element,
            partidaCode,
            new MetradoResult(
                Metrado: quantity,
                Raw: quantity,
                Gross: quantity,
                AppliedMode: appliedMode,
                AppliedThreshold: Defaults.AreaThresholdSquareMetres,
                ClampedToGross: false));
    }

    /// <summary>The grouped result the writer consumes.</summary>
    internal static TakeoffResult ResultOf(params Linea[] lineas) => TakeoffResult.Group(lineas);
}
