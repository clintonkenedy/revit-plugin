using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// Pins the built-in criteria for the six categories the add-in measures
/// (task 2.2): each with its own unit, quantity sources, threshold and mode,
/// and each configurable apart from the others (<c>takeoff-configuration</c>,
/// "External, Versionable Criteria File" and "Openings Threshold Is
/// Configurable per Category").
/// </summary>
public sealed class SixCategoryDefaultsTests
{
    [Fact]
    public void TheDefaultsCoverTheSixCategoriesExtractionReads()
    {
        Assert.Equal(
            ["Doors", "Floors", "Railings", "Roofs", "Walls", "Windows"],
            CriteriaSet.Default.ByCategory.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Areas for the hosts that openings cut; linear metres for railings, as a
    /// metrado states them ("ml"); a count for doors and windows, which have
    /// no quantity source at all (the empty list, N1). Nothing is added back
    /// where there are no openings, so those thresholds are zero.
    /// </summary>
    [Theory]
    [InlineData("Walls", QuantityUnit.SquareMetre, "HOST_AREA_COMPUTED", 1.0)]
    [InlineData("Floors", QuantityUnit.SquareMetre, "HOST_AREA_COMPUTED", 1.0)]
    [InlineData("Roofs", QuantityUnit.SquareMetre, "HOST_AREA_COMPUTED", 1.0)]
    [InlineData("Railings", QuantityUnit.Metre, "CURVE_ELEM_LENGTH", 0.0)]
    [InlineData("Doors", QuantityUnit.Each, null, 0.0)]
    [InlineData("Windows", QuantityUnit.Each, null, 0.0)]
    public void EachCategoryHasItsOwnUnitSourcesAndThreshold(string category, QuantityUnit unit, string? source, double threshold)
    {
        CategoryCriterion criterion = CriteriaSet.Default.ByCategory[category];

        Assert.Equal(unit, criterion.Unit);
        Assert.Equal(source is null ? [] : [source], criterion.Sources);
        Assert.Equal(threshold, criterion.Threshold.Value);
        Assert.Equal(unit, criterion.Threshold.Unit);
        Assert.Equal(BoundaryMode.Exclusive, criterion.Threshold.Mode);
    }

    [Fact]
    public void AMetreIsWrittenM()
    {
        Assert.Equal("m", QuantityUnit.Metre.Symbol());
    }

    /// <summary>"File overrides one category only": floors use the file, walls the built-in default.</summary>
    [Fact]
    public void AFileDefiningFloorsOnlyLeavesWallsOnTheBuiltInDefault()
    {
        CriteriaSet merged = Merged(new CategoryOverride("Floors", threshold: 0.5));

        Assert.Equal(CriteriaSet.Default.ByCategory["Walls"], merged.ByCategory["Walls"]);
        Assert.Equal(0.5, merged.ByCategory["Floors"].Threshold.Value);
    }

    /// <summary>"Different thresholds per category": a 0.8 m² opening is added back in the wall, not in the floor.</summary>
    [Fact]
    public void WallAndFloorThresholdsApplyIndependently()
    {
        CriteriaSet merged = Merged(new CategoryOverride("Walls", threshold: 1.0), new CategoryOverride("Floors", threshold: 0.5));

        Assert.Equal(18.8, Metrado(merged, Host("Walls", area: 18.0, opening: 0.8)), 9);
        Assert.Equal(30.0, Metrado(merged, Host("Floors", area: 30.0, opening: 0.8)), 9);
    }

    /// <summary>"Boundary mode set per category": the wall's 1.0 m² opening is added back, the floor's is not.</summary>
    [Fact]
    public void AnInclusiveWallModeLeavesTheFloorsOnTheDefaultMode()
    {
        CriteriaSet merged = Merged(new CategoryOverride("Walls", mode: BoundaryMode.Inclusive));

        Assert.Equal(19.0, Metrado(merged, Host("Walls", area: 18.0, opening: 1.0)), 9);
        Assert.Equal(30.0, Metrado(merged, Host("Floors", area: 30.0, opening: 1.0)), 9);
    }

    private static CriteriaSet Merged(params CategoryOverride[] overrides)
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(CriteriaSet.Default, overrides);
        Assert.True(merged.IsOk, merged.IsOk ? null : merged.Error.Message);
        return merged.Value;
    }

    private static ElementTakeoff Host(string category, double area, double opening) =>
        Wall() with
        {
            CategoryName = category,
            Quantities = [new RawQuantity("HOST_AREA_COMPUTED", SquareMetres(area))],
            Openings = [new OpeningQuantity("opening-1", SquareMetres(opening))],
        };

    private static double Metrado(CriteriaSet criteria, ElementTakeoff element)
    {
        MetradoOutcome outcome = Measurement.Measure(element, criteria.ByCategory[element.CategoryName]);
        Assert.Equal(MetradoStatus.Measured, outcome.Status);
        return outcome.Result!.Metrado.Value;
    }
}
