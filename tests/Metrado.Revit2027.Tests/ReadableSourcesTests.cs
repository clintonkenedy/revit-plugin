using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins that a run never measures a category by a source extraction does not
/// read. Such a source is empty on every element, and the run would say the
/// model lacks it — false, since the add-in never looked — so the criteria
/// are refused before the model is read.
/// </summary>
public sealed class ReadableSourcesTests
{
    /// <summary>Every source the built-in criteria name for an extracted category is one extraction reads.</summary>
    [Fact]
    public void TheBuiltInCriteriaReadOnlyWhatExtractionReads()
    {
        Assert.Null(ReadableSources.Check(CriteriaSet.Default));
        Assert.All(ReadableSources.ByCategory.Keys, key => Assert.True(CriteriaSet.Default.ByCategory.ContainsKey(key), key));
    }

    public static TheoryData<CategoryOverride, string[]> Unreadable() => new()
    {
        { new CategoryOverride("Doors", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"]), ["Doors", "HOST_AREA_COMPUTED", "counted"] },
        { new CategoryOverride("Windows", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"]), ["Windows", "HOST_AREA_COMPUTED", "counted"] },
        { new CategoryOverride("Walls", QuantityUnit.CubicMetre, ["HOST_VOLUME_COMPUTED"]), ["Walls", "HOST_VOLUME_COMPUTED", "reads HOST_AREA_COMPUTED"] },
        { new CategoryOverride("Railings", sources: ["X", "CURVE_ELEM_LENGTH"]), ["Railings", "from X,", "reads CURVE_ELEM_LENGTH"] },
        { new CategoryOverride("Floors", QuantityUnit.CubicMetre, ["HOST_VOLUME_COMPUTED"]), ["Floors", "HOST_VOLUME_COMPUTED", "reads HOST_AREA_COMPUTED"] },
        { new CategoryOverride("Roofs", QuantityUnit.CubicMetre, ["HOST_VOLUME_COMPUTED"]), ["Roofs", "HOST_VOLUME_COMPUTED", "reads HOST_AREA_COMPUTED"] },
    };

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void ASourceExtractionDoesNotReadIsRefused(CategoryOverride entry, string[] phrases)
    {
        ConfigError error = Assert.IsType<ConfigError>(ReadableSources.Check(Criteria(entry)));

        Assert.All(phrases, phrase => Assert.Contains(phrase, error.Message, StringComparison.Ordinal));
    }

    /// <summary>Only the unreadable sources are named: a readable one listed beside them is not the problem.</summary>
    [Fact]
    public void OnlyTheUnreadableSourcesAreNamed()
    {
        ConfigError error = Assert.IsType<ConfigError>(ReadableSources.Check(Criteria(new CategoryOverride("Railings", sources: ["X", "CURVE_ELEM_LENGTH"]))));

        Assert.StartsWith("The criteria ask Railings to be measured from X, ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A category extraction does not read at all has no sources to refuse.</summary>
    [Fact]
    public void ACategoryExtractionDoesNotReadIsNotJudged()
    {
        CategoryCriterion walls = CriteriaSet.Default.ByCategory["Walls"];
        CategoryCriterion ceilings = new("Ceilings", walls.Unit, ["ANYTHING"], walls.Threshold);

        Assert.Null(ReadableSources.Check(new CriteriaSet(new Dictionary<string, CategoryCriterion> { ["Ceilings"] = ceilings })));
    }

    /// <summary>A set without a category extraction reads is judged on the categories it has.</summary>
    [Fact]
    public void AMissingCategoryDoesNotEndTheCheck()
    {
        CategoryCriterion railings = Criteria(new CategoryOverride("Railings", sources: ["X"])).ByCategory["Railings"];

        Assert.NotNull(ReadableSources.Check(new CriteriaSet(new Dictionary<string, CategoryCriterion> { ["Railings"] = railings })));
    }

    /// <summary>The hosts are taken off by layer when the criteria ask, and only theirs are read so.</summary>
    [Theory]
    [InlineData("Walls")]
    [InlineData("Floors")]
    [InlineData("Roofs")]
    public void WallsFloorsAndRoofsMayBeTakenOffByLayer(string category)
    {
        CriteriaSet criteria = Criteria(new CategoryOverride(category, layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())));

        Assert.Null(ReadableSources.Check(criteria));
        Assert.Equal([category], ReadableSources.Layered(criteria));
    }

    /// <summary>Layers stated off, or left out, are not read: a run with no criteria file reads none.</summary>
    [Fact]
    public void OnlyTheCategoriesTakenOffByLayerHaveTheirLayersRead()
    {
        CriteriaSet criteria = CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride("Roofs", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())), new CategoryOverride("Floors", layers: LayerOverride.Off)]).Value;

        Assert.Equal(["Roofs"], ReadableSources.Layered(criteria));
        Assert.Empty(ReadableSources.Layered(CriteriaSet.Default));
    }

    [Fact]
    public void LayersStatedOffAreNoRefusal()
    {
        Assert.Null(ReadableSources.Check(Criteria(new CategoryOverride("Walls", layers: LayerOverride.Off))));
    }

    private static CriteriaSet Criteria(CategoryOverride entry) => CriteriaSet.Merge(CriteriaSet.Default, [entry]).Value;
}
