namespace Metrado.Domain.Tests;

/// <summary>
/// Shared setup for the partida and grouping tests.
/// </summary>
/// <remarks>
/// Every grouping scenario varies only the few facts the key is built from —
/// category, resolved code, and the type identity that must <em>not</em> reach the
/// key — while the rest of the takeoff is irrelevant noise. Building the line in
/// one place keeps the facts under test visible instead of buried under arguments
/// that never change.
/// </remarks>
internal static class TakeoffFixture
{
    /// <summary>One measured instance, ready to be grouped into a partida.</summary>
    internal static Linea Line(
        string uniqueId,
        string partidaCode,
        double amount = 10.0,
        string category = "Walls",
        string typeName = "Generic - 200mm",
        QuantityUnit unit = QuantityUnit.SquareMetre)
    {
        ElementTakeoff element = MeasurementFixture.Wall(uniqueId) with
        {
            CategoryName = category,
            TypeName = typeName,
            TypeKey = $"Basic Wall:{typeName}",
        };

        return new Linea(element, partidaCode, Measured(amount, unit));
    }

    /// <summary>
    /// A metrado result standing in for one the measurement rule produced.
    /// </summary>
    /// <remarks>
    /// The applied mode and threshold are parameters rather than constants because
    /// the run report must repeat what was <em>applied</em>, and a fixture that
    /// hardcoded them could not tell that apart from what was configured.
    /// </remarks>
    internal static MetradoResult Measured(
        double amount,
        QuantityUnit unit = QuantityUnit.SquareMetre,
        BoundaryMode appliedMode = BoundaryMode.Exclusive,
        double appliedThreshold = 1.0)
    {
        Quantity quantity = new(amount, unit);

        return new MetradoResult(
            Metrado: quantity,
            Raw: quantity,
            Gross: quantity,
            AppliedMode: appliedMode,
            AppliedThreshold: appliedThreshold,
            ClampedToGross: false);
    }

    /// <summary>
    /// A run of instances of one element type, every one of them resolving to the
    /// same partida code.
    /// </summary>
    internal static Linea[] Instances(
        string typeName,
        int count,
        string partidaCode,
        double amount) =>
        [
            .. Enumerable
                .Range(1, count)
                .Select(n => Line($"{typeName}-{n}", partidaCode, amount, typeName: typeName)),
        ];
}
