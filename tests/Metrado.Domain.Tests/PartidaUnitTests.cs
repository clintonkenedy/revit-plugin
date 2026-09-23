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
        Assert.Contains("Partida C1010 in Walls has lines in m2 and in u.", warning.Condition);
        Assert.Contains("not added together", warning.Condition);
    }

    /// <summary>
    /// Revit's iteration order decides nothing: the same lines in another order
    /// give the same warning, naming the first element of the other unit by
    /// UniqueId and the units in a fixed order.
    /// </summary>
    [Fact]
    public void TheWarningDoesNotDependOnArrivalOrder()
    {
        Linea[] lines = [Line("w-1", 10.0, QuantityUnit.SquareMetre), Line("w-2", 1.0, QuantityUnit.Each), Line("w-3", 1.0, QuantityUnit.Each)];

        ValidationWarning inOrder = Assert.Single(TakeoffResult.Group(lines).Warnings);
        ValidationWarning reversed = Assert.Single(TakeoffResult.Group([.. lines.Reverse()]).Warnings);

        Assert.Equal(inOrder, reversed);
        Assert.Equal("w-2", inOrder.UniqueId);
    }

    /// <summary>Unclassified lines are not a partida; their split is said as it is.</summary>
    [Fact]
    public void UnclassifiedLinesInTwoUnitsAreNotCalledAPartida()
    {
        TakeoffResult result = TakeoffResult.Group(
            [Line("w-1", 10.0, QuantityUnit.SquareMetre, UnclassifiedResolver.Code), Line("w-2", 1.0, QuantityUnit.Each, UnclassifiedResolver.Code)]);

        ValidationWarning warning = Assert.Single(result.Warnings);
        Assert.StartsWith("Unclassified lines in Walls come in m2 and in u.", warning.Condition);
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

    private static Linea Line(string uniqueId, double value, QuantityUnit unit, string code = "C1010")
    {
        Quantity quantity = new(value, unit);
        return new Linea(
            Wall(uniqueId),
            code,
            new MetradoResult(quantity, quantity, quantity, BoundaryMode.Exclusive, 0, ClampedToGross: false));
    }
}
