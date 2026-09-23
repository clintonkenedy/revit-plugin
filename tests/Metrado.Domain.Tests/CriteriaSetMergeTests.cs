using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>takeoff-configuration</c>, requirement "External, Versionable Criteria File":
/// "Values present in the file SHALL override the built-in defaults per category;
/// categories absent from the file SHALL keep their defaults."
/// </summary>
/// <remarks>
/// The merge is per-category and then per-field, and both halves have to be proved
/// separately. A merge that replaced a whole category would satisfy every
/// assertion about a category the file did not mention, and still silently discard
/// the unit and sources of one it did.
/// </remarks>
public sealed class CriteriaSetMergeTests
{
    private static CriteriaSet TwoCategories() =>
        new(new Dictionary<string, CategoryCriterion>(StringComparer.Ordinal)
        {
            ["Walls"] = new CategoryCriterion(
                "Walls",
                QuantityUnit.SquareMetre,
                ["HOST_AREA_COMPUTED"],
                Threshold(1.0)),
            ["Floors"] = new CategoryCriterion(
                "Floors",
                QuantityUnit.SquareMetre,
                ["HOST_AREA_COMPUTED", "Metrado_Area"],
                Threshold(0.5, BoundaryMode.Inclusive)),
        });

    private static CriteriaSet Merged(CriteriaSet defaults, params CategoryOverride[] overrides)
    {
        Result<CriteriaSet, ConfigError> result = CriteriaSet.Merge(defaults, overrides);

        Assert.True(result.IsOk, $"Expected the merge to succeed, but it failed: {result}");
        return result.Value;
    }

    private static ConfigError Rejected(CriteriaSet defaults, params CategoryOverride[] overrides)
    {
        Result<CriteriaSet, ConfigError> result = CriteriaSet.Merge(defaults, overrides);

        Assert.False(result.IsOk, $"Expected the merge to be rejected, but it produced: {result}");
        return result.Error;
    }

    private static void AssertSameCriterion(CategoryCriterion expected, CategoryCriterion actual)
    {
        Assert.Equal(expected.Category, actual.Category);
        Assert.Equal(expected.Unit, actual.Unit);
        Assert.Equal(expected.Sources, actual.Sources);
        Assert.Equal(expected.Threshold, actual.Threshold);
    }

    /// <summary>
    /// "Categories absent from the file SHALL keep their defaults."
    /// </summary>
    /// <remarks>
    /// The requirement's own scenario, "File overrides one category only": a file
    /// defining a criterion for Floors only leaves walls on the built-in default.
    /// Every field of the untouched category is checked, because "keeps its default"
    /// means all of it and not merely its name.
    /// </remarks>
    [Fact]
    public void ACategoryTheOverridesDoNotMentionKeepsItsDefaultEntirely()
    {
        CriteriaSet defaults = TwoCategories();

        CriteriaSet merged = Merged(defaults, new CategoryOverride("Floors", threshold: 0.25));

        AssertSameCriterion(defaults.ByCategory["Walls"], merged.ByCategory["Walls"]);
        Assert.Equal(0.25, merged.ByCategory["Floors"].Threshold.Value, 9);
    }

    /// <summary>
    /// No overrides at all is the "no configuration file" shape, and it changes
    /// nothing.
    /// </summary>
    [Fact]
    public void MergingNoOverridesLeavesEveryBuiltInCriterionExactlyAsItWas()
    {
        CriteriaSet merged = Merged(CriteriaSet.Default);

        Assert.Equal(CriteriaSet.Default.ByCategory.Count, merged.ByCategory.Count);
        AssertSameCriterion(CriteriaSet.Default.ByCategory["Walls"], merged.ByCategory["Walls"]);
    }

    /// <summary>
    /// "A present category inherits every field left `null` — threshold, mode, unit
    /// and sources alike."
    /// </summary>
    /// <remarks>
    /// An override that sets nothing is the strongest statement of the per-field
    /// rule: a merge that replaced the category wholesale would leave a criterion
    /// with no unit, no sources and a zero threshold, which measures every wall in
    /// the model at its raw Revit area.
    /// </remarks>
    [Fact]
    public void AnOverrideThatSetsNothingInheritsEveryFieldOfItsDefault()
    {
        CriteriaSet defaults = TwoCategories();

        CriteriaSet merged = Merged(defaults, new CategoryOverride("Walls"));

        AssertSameCriterion(defaults.ByCategory["Walls"], merged.ByCategory["Walls"]);
    }

    /// <summary>
    /// A threshold override changes the threshold and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, "Openings Threshold Is Configurable per
    /// Category": "A category without an explicit threshold SHALL inherit the
    /// default." The converse is asserted here field by field — unit, sources and
    /// mode all survive a threshold-only override.
    /// </remarks>
    [Fact]
    public void AThresholdOverrideLeavesTheUnitSourcesAndModeInherited()
    {
        CriteriaSet defaults = TwoCategories();
        CategoryCriterion baseline = defaults.ByCategory["Floors"];

        CategoryCriterion floors =
            Merged(defaults, new CategoryOverride("Floors", threshold: 2.5)).ByCategory["Floors"];

        Assert.Equal(2.5, floors.Threshold.Value, 9);
        Assert.Equal(baseline.Unit, floors.Unit);
        Assert.Equal(baseline.Sources, floors.Sources);
        Assert.Equal(baseline.Threshold.Mode, floors.Threshold.Mode);
    }

    /// <summary>
    /// A mode override changes the mode and nothing else.
    /// </summary>
    /// <remarks>
    /// The mode is overridden here from <c>Exclusive</c> to <c>Inclusive</c> on a
    /// category whose default is <c>Exclusive</c>, so the assertion cannot pass by
    /// coincidence with the inherited value.
    /// </remarks>
    [Fact]
    public void AModeOverrideLeavesTheThresholdUnitAndSourcesInherited()
    {
        CriteriaSet defaults = TwoCategories();
        CategoryCriterion baseline = defaults.ByCategory["Walls"];

        CategoryCriterion walls = Merged(
            defaults,
            new CategoryOverride("Walls", mode: BoundaryMode.Inclusive)).ByCategory["Walls"];

        Assert.Equal(BoundaryMode.Inclusive, walls.Threshold.Mode);
        Assert.Equal(baseline.Threshold.Value, walls.Threshold.Value, 9);
        Assert.Equal(baseline.Unit, walls.Unit);
        Assert.Equal(baseline.Sources, walls.Sources);
    }

    /// <summary>
    /// A sources override changes the sources and nothing else.
    /// </summary>
    [Fact]
    public void ASourcesOverrideLeavesTheUnitThresholdAndModeInherited()
    {
        CriteriaSet defaults = TwoCategories();
        CategoryCriterion baseline = defaults.ByCategory["Walls"];

        CategoryCriterion walls = Merged(
            defaults,
            new CategoryOverride("Walls", sources: ["Metrado_Area", "HOST_AREA_COMPUTED"]))
            .ByCategory["Walls"];

        Assert.Equal(new[] { "Metrado_Area", "HOST_AREA_COMPUTED" }, walls.Sources);
        Assert.Equal(baseline.Unit, walls.Unit);
        Assert.Equal(baseline.Threshold, walls.Threshold);
    }

    /// <summary>
    /// A unit override changes the unit, and the inherited threshold follows it.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>: the threshold is "expressed in that category's
    /// measurement unit". Carrying the old unit on an inherited threshold would
    /// leave the criterion measuring m³ while its threshold still claimed m², and
    /// <c>Measurement.Apply</c> refuses that comparison outright — so every element
    /// in the category would report <c>UnitMismatch</c> from a file that named only
    /// a unit.
    /// </remarks>
    [Fact]
    public void AUnitOverrideCarriesTheInheritedThresholdIntoTheNewUnit()
    {
        CriteriaSet defaults = TwoCategories();
        CategoryCriterion baseline = defaults.ByCategory["Walls"];

        CategoryCriterion walls = Merged(
            defaults,
            new CategoryOverride("Walls", unit: QuantityUnit.CubicMetre)).ByCategory["Walls"];

        Assert.Equal(QuantityUnit.CubicMetre, walls.Unit);
        Assert.Equal(QuantityUnit.CubicMetre, walls.Threshold.Unit);
        Assert.Equal(baseline.Threshold.Value, walls.Threshold.Value, 9);
        Assert.Equal(baseline.Sources, walls.Sources);
    }

    /// <summary>
    /// A criterion produced by the merge is usable by the measurement rule.
    /// </summary>
    /// <remarks>
    /// The per-field assertions above describe a shape; this one spends it. A wall
    /// with a 1.0 m² opening reads 18.0 m² under the inherited <c>exclusive</c>
    /// default and 19.0 m² once the file sets <c>inclusive</c> — the two defensible
    /// budgets the boundary mode exists to distinguish, both produced from the same
    /// model by the same merge.
    /// </remarks>
    [Fact]
    public void AMergedCriterionMeasuresWithTheOverriddenConventionAndTheInheritedThreshold()
    {
        ElementTakeoff wall = WallWith(Source("HOST_AREA_COMPUTED", 18.0)) with
        {
            Openings = [new OpeningQuantity("door-1", SquareMetres(1.0))],
        };
        CriteriaSet defaults = TwoCategories();

        MetradoOutcome inherited = Measurement.Measure(wall, defaults.ByCategory["Walls"]);
        MetradoOutcome overridden = Measurement.Measure(
            wall,
            Merged(defaults, new CategoryOverride("Walls", mode: BoundaryMode.Inclusive))
                .ByCategory["Walls"]);

        Assert.Equal(18.0, inherited.Result!.Metrado.Value, 9);
        Assert.Equal(19.0, overridden.Result!.Metrado.Value, 9);
    }

    /// <summary>
    /// An explicitly empty source list is an override, not an absence.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means "inherit" and <c>[]</c> means "this category lists no
    /// quantity source" — which is how I2's count-based categories are declared.
    /// Collapsing the two, as any emptiness test rather than a null test would,
    /// makes a counted category impossible to express: it would silently inherit
    /// the area sources of whatever it was defaulted from.
    /// </remarks>
    [Fact]
    public void AnExplicitlyEmptySourceListOverridesRatherThanInherits()
    {
        CriteriaSet defaults = TwoCategories();

        // Counted, so in units (task 2.3): an empty list in m2 is refused.
        CategoryCriterion walls = Merged(
            defaults,
            new CategoryOverride("Walls", unit: QuantityUnit.Each, sources: [])).ByCategory["Walls"];

        Assert.Empty(walls.Sources);
    }

    /// <summary>
    /// <c>takeoff-configuration</c>, requirement "Invalid Configuration Fails
    /// Loudly", scenario "Unknown category in configuration": "the run stops with an
    /// error naming that category".
    /// </summary>
    [Fact]
    public void AnOverrideForACategoryTheProductDoesNotSupportIsRejectedByName()
    {
        ConfigError error = Rejected(TwoCategories(), new CategoryOverride("Widgets"));

        Assert.Equal("Widgets", error.Category);
        Assert.Contains("Widgets", error.Message);
    }

    /// <summary>
    /// An unknown category is rejected even when the rest of the file is valid.
    /// </summary>
    /// <remarks>
    /// The system "MUST NOT silently fall back to defaults when a file was
    /// supplied", and a merge that applied the entries it recognised and dropped
    /// the one it did not would do exactly that for the dropped category.
    /// </remarks>
    [Fact]
    public void OneUnknownCategoryStopsTheWholeMergeAndNotJustItsOwnEntry()
    {
        Result<CriteriaSet, ConfigError> result = CriteriaSet.Merge(
            TwoCategories(),
            [new CategoryOverride("Walls", threshold: 2.0), new CategoryOverride("Widgets")]);

        Assert.False(result.IsOk);
        Assert.Equal("Widgets", result.Error.Category);
    }

    /// <summary>
    /// Unknown category names are rejected <em>before</em> any field is coalesced.
    /// </summary>
    /// <remarks>
    /// The override below is invalid twice over: it names a category the product
    /// does not support, and it carries a negative threshold. Which of the two the
    /// user is told about is the observable difference between validating first and
    /// coalescing first — and naming the threshold would send them to fix a number
    /// in an entry that should not exist at all.
    /// </remarks>
    [Fact]
    public void AnUnknownCategoryIsReportedAsUnknownRatherThanAsAnInvalidThreshold()
    {
        ConfigError error = Rejected(
            TwoCategories(),
            new CategoryOverride("Widgets", threshold: -1.0));

        Assert.Equal("Widgets", error.Category);
        Assert.Contains("Widgets", error.Message);
        Assert.DoesNotContain("negative", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Category names are matched exactly, so a difference in case is an unknown
    /// category rather than a quiet match.
    /// </summary>
    /// <remarks>
    /// Revit category names have one spelling. Matching loosely would have to decide
    /// what a file declaring both <c>Walls</c> and <c>walls</c> means; matching
    /// exactly makes that question unreachable, and the user is told which spelling
    /// the product did not recognise.
    /// </remarks>
    [Fact]
    public void ACategoryNameDifferingOnlyInCaseIsNotSilentlyMatched()
    {
        ConfigError error = Rejected(TwoCategories(), new CategoryOverride("walls"));

        Assert.Equal("walls", error.Category);
        Assert.Equal(1.0, TwoCategories().ByCategory["Walls"].Threshold.Value, 9);
    }

    /// <summary>
    /// The same category declared twice is ambiguous configuration, so it is
    /// refused.
    /// </summary>
    /// <remarks>
    /// Beyond the literal task text, and deliberate. The two entries below disagree
    /// about the threshold, and every silent answer is indefensible: taking the last
    /// hides the first, taking the first hides the last, and coalescing both against
    /// the default makes the result depend on which fields each happened to set.
    /// The user is told to pick one, in keeping with the requirement that invalid
    /// configuration fails loudly rather than producing a confidently wrong budget.
    /// </remarks>
    [Fact]
    public void TheSameCategoryDeclaredTwiceIsRefusedRatherThanQuietlyResolved()
    {
        ConfigError error = Rejected(
            TwoCategories(),
            new CategoryOverride("Walls", threshold: 2.0),
            new CategoryOverride("Walls", threshold: 3.0));

        Assert.Equal("Walls", error.Category);
        Assert.Contains("Walls", error.Message);
        Assert.Contains("more than once", error.Message);
    }

    /// <summary>
    /// <c>takeoff-configuration</c>, scenario "Negative threshold is rejected":
    /// "loading fails with an error naming the category and the invalid value".
    /// </summary>
    [Fact]
    public void ANegativeThresholdIsRejectedNamingTheCategoryAndTheValue()
    {
        ConfigError error = Rejected(
            TwoCategories(),
            new CategoryOverride("Floors", threshold: -1.0));

        Assert.Equal("Floors", error.Category);
        Assert.Equal("-1", error.InvalidValue);
    }

    /// <summary>
    /// <c>takeoff-configuration</c>, scenario "Invalid boundary mode is rejected".
    /// </summary>
    /// <remarks>
    /// A mode outside the closed domain cannot come from the I2 parser, which maps
    /// two spellings and rejects the rest. It can come from a cast, and the merge is
    /// the last place that can still name the category responsible.
    /// </remarks>
    [Fact]
    public void ABoundaryModeOutsideTheClosedDomainIsRejectedNamingTheCategory()
    {
        ConfigError error = Rejected(
            TwoCategories(),
            new CategoryOverride("Walls", mode: (BoundaryMode)7));

        Assert.Equal("Walls", error.Category);
        Assert.Contains("Exclusive", error.Message);
        Assert.Contains("Inclusive", error.Message);
    }

    /// <summary>
    /// <c>takeoff-configuration</c>, "Invalid Configuration Fails Loudly": a file
    /// declaring "an unsupported unit" stops the run.
    /// </summary>
    /// <remarks>
    /// An undeclared unit would otherwise travel all the way into the workbook,
    /// where <c>QuantityUnit.Symbol()</c> throws while writing the row — a crash
    /// mid-export instead of a message before it.
    /// </remarks>
    [Fact]
    public void AUnitOutsideTheClosedSetIsRejectedNamingTheCategory()
    {
        ConfigError error = Rejected(
            TwoCategories(),
            new CategoryOverride("Walls", unit: (QuantityUnit)42));

        Assert.Equal("Walls", error.Category);
        Assert.Equal("42", error.InvalidValue);
    }

    /// <summary>
    /// The merge is a function, not an edit: the defaults it was given are the same
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// <see cref="CriteriaSet.Default"/> is a process-wide constant, so a merge that
    /// wrote through to its argument would make the second export in a session
    /// inherit the first one's configuration file.
    /// </remarks>
    [Fact]
    public void MergingDoesNotWriteThroughToTheDefaultsItWasGiven()
    {
        CriteriaSet defaults = TwoCategories();

        Merged(defaults, new CategoryOverride("Walls", threshold: 9.0, sources: ["Other"]));

        Assert.Equal(1.0, defaults.ByCategory["Walls"].Threshold.Value, 9);
        Assert.Equal(new[] { "HOST_AREA_COMPUTED" }, defaults.ByCategory["Walls"].Sources);
    }

    /// <summary>
    /// Every default category survives the merge, including the ones that were
    /// overridden.
    /// </summary>
    /// <remarks>
    /// A merge that returned only the overridden entries would pass every assertion
    /// about the criteria it produced, and leave the model's other categories with
    /// no criterion at all.
    /// </remarks>
    [Fact]
    public void TheMergedSetStillCoversEveryCategoryTheDefaultsDefined()
    {
        CriteriaSet defaults = TwoCategories();

        CriteriaSet merged = Merged(defaults, new CategoryOverride("Walls", threshold: 2.0));

        Assert.Equal(defaults.ByCategory.Count, merged.ByCategory.Count);
        Assert.True(merged.ByCategory.ContainsKey("Walls"));
        Assert.True(merged.ByCategory.ContainsKey("Floors"));
    }
}
