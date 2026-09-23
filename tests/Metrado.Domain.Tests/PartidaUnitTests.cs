using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>excel-budget-export</c>, "Unit Reported per Partida": lines within one
/// partida must share a unit, and a mismatch "MUST raise a validation warning
/// rather than sum incompatible quantities" (task 2.5). Grouping lists each
/// unit's lines as a partida of their own and says so; nothing is converted
/// and nothing is added across units.
/// </summary>
public sealed class PartidaUnitTests
{
    [Fact]
    public void LinesOfOnePartidaInTwoUnitsAreListedApartAndNotSummed()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            Line("w-1", 10.0, QuantityUnit.SquareMetre),
            Line("w-2", 5.0, QuantityUnit.SquareMetre),
            Line("w-3", 1.0, QuantityUnit.Each),
        ]);

        Assert.Equal(
            [new Quantity(15.0, QuantityUnit.SquareMetre), new Quantity(1.0, QuantityUnit.Each)],
            result.Partidas.Select(partida => partida.Total));
        Assert.All(result.Partidas, partida => Assert.Equal("C1010", partida.Key.PartidaCode));

        ValidationWarning warning = Assert.Single(result.Warnings);
        Assert.Equal("w-3", warning.UniqueId);
        Assert.Contains("C1010", warning.Condition);
        Assert.Contains("m2", warning.Condition);
        Assert.Contains("u", warning.Condition);
        Assert.Contains("not added together", warning.Condition);
    }

    /// <summary>The run's report carries the split's warning after the run's own, with no caller having to add it.</summary>
    [Fact]
    public void TheRunReportCarriesTheSplitsWarning()
    {
        TakeoffResult result = TakeoffResult.Group([Line("w-1", 10.0, QuantityUnit.SquareMetre), Line("w-2", 1.0, QuantityUnit.Each)]);
        ValidationWarning earlier = ValidationWarning.ForElement(Wall("w-0"), "measured before grouping");

        RunReport report = RunReport.For(result, [earlier]);

        Assert.Equal([earlier, result.Warnings[0]], report.Warnings);
    }

    [Fact]
    public void APartidaInOneUnitRaisesNoWarning()
    {
        TakeoffResult result = TakeoffResult.Group([Line("w-1", 10.0, QuantityUnit.SquareMetre), Line("w-2", 5.0, QuantityUnit.SquareMetre)]);

        Assert.Single(result.Partidas);
        Assert.Empty(result.Warnings);
    }

    private static Linea Line(string uniqueId, double value, QuantityUnit unit)
    {
        Quantity quantity = new(value, unit);
        return new Linea(
            Wall(uniqueId),
            "C1010",
            new MetradoResult(quantity, quantity, quantity, BoundaryMode.Exclusive, 0, ClampedToGross: false));
    }
}
