namespace Metrado.Domain.Tests;

/// <summary>
/// One partida: the lines that resolved to its code, the total they add up to,
/// and the unit that total is expressed in.
/// </summary>
public sealed class PartidaTests
{
    [Fact]
    public void ThePartidaTotalIsTheSumOfItsMeasurementLines()
    {
        Partida partida = Coded(
            "C1010",
            [
                .. TakeoffFixture.Instances("WT-A", count: 3, partidaCode: "C1010", amount: 10.0),
                .. TakeoffFixture.Instances("WT-B", count: 2, partidaCode: "C1010", amount: 4.0),
            ]);

        Assert.Equal(38.0, partida.Total.Value, 9);
        Assert.Equal(QuantityUnit.SquareMetre, partida.Total.Unit);
    }

    [Fact]
    public void ThePartidaTotalsTheLinesItWasGivenAndNotSomeFixedFigure()
    {
        Partida partida = Coded(
            "C1010",
            [
                TakeoffFixture.Line("wall-1", "C1010", amount: 10.0),
                TakeoffFixture.Line("wall-2", "C1010", amount: 2.5),
            ]);

        Assert.Equal(12.5, partida.Total.Value, 9);
    }

    [Fact]
    public void ThePartidaTotalCarriesTheUnitItsLinesWereMeasuredIn()
    {
        Partida partida = Coded(
            "C1010",
            [TakeoffFixture.Line("wall-1", "C1010", 3.0, unit: QuantityUnit.CubicMetre)]);

        Assert.Equal(QuantityUnit.CubicMetre, partida.Total.Unit);
        Assert.Equal(QuantityUnit.CubicMetre, partida.Unit);
    }

    [Fact]
    public void LinesMeasuredInDifferentUnitsAreRefusedRatherThanSummedIntoOneTotal()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () =>
                Coded(
                    "C1010",
                    [
                        TakeoffFixture.Line("wall-1", "C1010", 10.0, unit: QuantityUnit.SquareMetre),
                        TakeoffFixture.Line("wall-2", "C1010", 3.0, unit: QuantityUnit.CubicMetre),
                    ]));

        Assert.Contains("C1010", error.Message, StringComparison.Ordinal);
        Assert.Contains("m2", error.Message, StringComparison.Ordinal);
        Assert.Contains("m3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APartidaWithNoMeasurementLinesIsRefusedBecauseItHasNoUnitAndNoTotal()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Coded("C1010", []));

        Assert.Contains("C1010", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePartidaHoldingUncodeableElementsKnowsItIsTheUnclassifiedGroup()
    {
        Partida unclassified = Coded(
            UnclassifiedResolver.Code,
            [TakeoffFixture.Line("wall-1", UnclassifiedResolver.Code)]);

        Assert.True(unclassified.IsUnclassified);
        Assert.False(Coded("C1010", [TakeoffFixture.Line("wall-2", "C1010")]).IsUnclassified);
    }

    private static Partida Coded(string partidaCode, IReadOnlyList<Linea> lineas) =>
        new(new PartidaKey("Walls", partidaCode), lineas);
}
