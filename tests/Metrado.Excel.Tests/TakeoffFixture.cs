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

    /// <summary>
    /// One material of a layered wall, measured in m2: its layers placed in a
    /// wall's usual order (exterior finish first, interior finish last), the
    /// material's id taken from its name so one material keeps one id.
    /// </summary>
    internal static Linea LayerLine(string uniqueId, string partidaCode, double metrado, string material, params (LayerFunction Function, double WidthMetres)[] layers)
    {
        string id = "mat-" + string.Concat(material.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        Linea line = Line(uniqueId, partidaCode, metrado);
        return line with
        {
            Layer = new MaterialLayers(
                new MaterialRef(id, material),
                [.. layers.Select(layer => new CompoundLayer(Position(layer.Function), layer.Function, new Quantity(layer.WidthMetres, QuantityUnit.Metre), id))]),
        };
    }

    private static int Position(LayerFunction function) => function switch
    {
        LayerFunction.Finish1 => 0,
        LayerFunction.Substrate => 1,
        LayerFunction.Insulation => 2,
        LayerFunction.Structure or LayerFunction.StructuralDeck => 3,
        LayerFunction.Membrane => 4,
        _ => 5,
    };

    /// <summary>The grouped result the writer consumes.</summary>
    internal static TakeoffResult ResultOf(params Linea[] lineas) => TakeoffResult.Group(lineas);
}
