using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// N1, count-based measurement (task 2.3). <c>metrado-measurement</c>:
/// "Categories measured by count SHALL have no quantity source; their metrado
/// is the number of qualifying instances, with unit <c>u</c>." An empty
/// sources list means exactly that, and it is decided before any source is
/// looked for, so a counted category is never confused with one whose listed
/// sources all came up empty.
/// </summary>
public sealed class MeasurementCountedTests
{
    private static CategoryCriterion Doors => CriteriaSet.Default.ByCategory["Doors"];

    [Fact]
    public void AnElementOfACountedCategoryCountsAsOneUnit()
    {
        MetradoOutcome outcome = Measurement.Measure(Door("door-1"), Doors);

        Assert.Equal(MetradoStatus.Counted, outcome.Status);
        Assert.Equal(new Quantity(1, QuantityUnit.Each), outcome.Result?.Metrado);
        Assert.Null(outcome.Warning);
    }

    /// <summary>
    /// "Doors are counted": 14 doors resolving to one partida total 14 u. The
    /// design before N1 read no source for them and raised 14 warnings.
    /// </summary>
    [Fact]
    public void FourteenDoorsOfOnePartidaTotalFourteenUnitsAndRaiseNoWarning()
    {
        List<MetradoOutcome> outcomes = [.. Enumerable.Range(1, 14).Select(i => Measurement.Measure(Door($"door-{i}"), Doors))];

        TakeoffResult result = TakeoffResult.Group(
            [.. outcomes.Select((outcome, i) => new Linea(Door($"door-{i + 1}"), "C1010", outcome.Result!))]);

        Partida partida = Assert.Single(result.Partidas);
        Assert.Equal(new Quantity(14, QuantityUnit.Each), partida.Total);
        Assert.All(outcomes, outcome => Assert.Null(outcome.Warning));
    }

    /// <summary>Listed sources that all came up empty are still "no source", never a count.</summary>
    [Fact]
    public void ListedSourcesWithNoValueAreNotACount()
    {
        MetradoOutcome outcome = Measurement.Measure(Door("door-1"), CriteriaSet.Default.ByCategory["Walls"]);

        Assert.Equal(MetradoStatus.NoSource, outcome.Status);
    }

    /// <summary>An opening cut in a counted element changes nothing: an instance counts once.</summary>
    [Fact]
    public void OpeningsDoNotChangeACount()
    {
        ElementTakeoff door = Door("door-1") with { Openings = [new OpeningQuantity("vision-panel", SquareMetres(0.2))] };

        Assert.Equal(1, Measurement.Measure(door, Doors).Result?.Metrado.Value);
    }

    /// <summary>A category with no quantity source is counted, and a count is in units: m2 of doors is meaningless.</summary>
    [Fact]
    public void ACountedCriterionOutsideUnitsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new CategoryCriterion("Doors", QuantityUnit.SquareMetre, [], Threshold(0)));
    }

    [Theory]
    [InlineData("Doors", QuantityUnit.SquareMetre, null)]
    [InlineData("Walls", null, true)]
    public void AFileThatCountsInAnotherUnitIsRefusedNamingTheCategory(string category, QuantityUnit? unit, bool? noSources)
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride(category, unit: unit, sources: noSources is true ? [] : null)]);

        Assert.False(merged.IsOk);
        Assert.Equal(category, merged.Error.Category);
        Assert.Contains("counted", merged.Error.Message);
    }

    /// <summary>Doors given a source and a unit are measured like any other category.</summary>
    [Fact]
    public void AFileCanMeasureDoorsByASourceInstead()
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride("Doors", unit: QuantityUnit.SquareMetre, sources: ["HOST_AREA_COMPUTED"])]);

        Assert.True(merged.IsOk, merged.IsOk ? null : merged.Error.Message);
        ElementTakeoff door = Door("door-1") with { Quantities = [new RawQuantity("HOST_AREA_COMPUTED", SquareMetres(2.1))] };
        Assert.Equal(MetradoStatus.Measured, Measurement.Measure(door, merged.Value.ByCategory["Doors"]).Status);
    }

    private static ElementTakeoff Door(string uniqueId) =>
        Wall(uniqueId) with { CategoryName = "Doors", FamilyName = "Single-Flush", TypeName = "0915 x 2134mm" };
}
