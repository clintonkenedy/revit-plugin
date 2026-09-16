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
}
