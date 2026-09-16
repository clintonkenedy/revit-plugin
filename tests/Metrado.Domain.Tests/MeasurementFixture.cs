namespace Metrado.Domain.Tests;

/// <summary>
/// Shared setup for the measurement rule's tests.
/// </summary>
/// <remarks>
/// The rule takes an element purely so its warnings can name one, and almost
/// every scenario cares about nothing but the arithmetic. Building the element
/// and the threshold in one place keeps the numbers under test visible instead of
/// buried under arguments that never vary.
/// </remarks>
internal static class MeasurementFixture
{
    internal static ElementTakeoff Wall(string uniqueId = "wall-1") =>
        new(
            UniqueId: uniqueId,
            CategoryName: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeKey: "Basic Wall:Generic - 200mm",
            Codes: new CodificationReadings(
                assemblyCode: null,
                keynote: null,
                sharedParameters: new Dictionary<string, string?>()),
            Quantities: [],
            Openings: []);

    /// <summary>
    /// A threshold built through the only door in, so a test can never set up a
    /// configuration the product would have rejected.
    /// </summary>
    internal static OpeningsThreshold Threshold(
        double value,
        BoundaryMode mode = BoundaryMode.Exclusive,
        QuantityUnit unit = QuantityUnit.SquareMetre)
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(value, unit, mode, "Walls");

        Assert.True(result.IsOk, $"Test setup built an invalid threshold: {result}");
        return result.Value;
    }

    internal static Quantity SquareMetres(double value) => new(value, QuantityUnit.SquareMetre);

    internal static Quantity CubicMetres(double value) => new(value, QuantityUnit.CubicMetre);

    /// <summary>A wall carrying the quantities Revit computed for it.</summary>
    internal static ElementTakeoff WallWith(params RawQuantity[] quantities) =>
        Wall() with { Quantities = quantities };

    /// <summary>
    /// One whole-element quantity read from a named Revit parameter.
    /// </summary>
    internal static RawQuantity Source(string sourceKey, double squareMetres) =>
        new(sourceKey, SquareMetres(squareMetres));

    /// <summary>
    /// One quantity belonging to a single material layer rather than to the whole
    /// element — what I3's layer takeoff will emit alongside the element's own.
    /// </summary>
    internal static RawQuantity LayerSource(
        string sourceKey,
        double squareMetres,
        string materialName) =>
        new(
            sourceKey,
            SquareMetres(squareMetres),
            new MaterialRef($"material-{materialName}", materialName));

    /// <summary>
    /// A criterion over the given sources, in the order the criterion lists them.
    /// </summary>
    internal static CategoryCriterion Criterion(params string[] sources) =>
        new("Walls", QuantityUnit.SquareMetre, sources, Threshold(1.0));

    /// <summary>
    /// The quantity a selection carries, failing the test when it carries none.
    /// </summary>
    /// <remarks>
    /// Goes through <see cref="SourceSelection.Match{T}"/> like every other caller,
    /// because that is the only way to reach the quantity. A test helper that
    /// reached it some other way would be exercising a door the product does not
    /// have.
    /// </remarks>
    internal static Quantity Selected(SourceSelection selection) =>
        selection.Match(
            selected: quantity => quantity,
            none: () => throw new Xunit.Sdk.XunitException(
                "Expected a source to be selected, but the selection carried none."));
}
