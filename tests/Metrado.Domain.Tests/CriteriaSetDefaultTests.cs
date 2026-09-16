using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>takeoff-configuration</c>, requirement "Usable Defaults Without Any
/// Configuration": the add-in "SHALL ship built-in default criteria and a default
/// openings threshold, and SHALL run correctly with no configuration file present.
/// Absence of configuration MUST NOT be an error."
/// </summary>
/// <remarks>
/// The built-in set is the product's answer to a fresh installation, so these
/// tests are about a first run, not about a data structure. The load-bearing one
/// is <see cref="AFirstRunWithNoConfigurationMeasuresAWallWithTheOpeningsCorrectionApplied"/>:
/// it measures a real wall through the defaults and asserts a number that differs
/// from the raw Revit area.
/// </remarks>
public sealed class CriteriaSetDefaultTests
{
    private static CategoryCriterion DefaultWalls => CriteriaSet.Default.ByCategory["Walls"];

    /// <summary>
    /// <c>metrado-measurement</c>, requirement "Per-Category Measurement Criterion":
    /// "In I1 the criterion for Walls is fixed: area in m² with the openings
    /// threshold applied."
    /// </summary>
    [Fact]
    public void TheBuiltInDefaultsMeasureWallsAsAreaInSquareMetres()
    {
        Assert.True(CriteriaSet.Default.ByCategory.ContainsKey("Walls"));
        Assert.Equal(QuantityUnit.SquareMetre, DefaultWalls.Unit);
        Assert.Equal("Walls", DefaultWalls.Category);
    }

    /// <summary>
    /// The category name is spelled exactly as extraction reports it, because the
    /// criterion is found by that name and nothing else.
    /// </summary>
    /// <remarks>
    /// A default whose key does not match <see cref="ElementTakeoff.CategoryName"/>
    /// is a default no element can ever be measured with — the lookup simply misses
    /// and every wall in the model goes unmeasured.
    /// </remarks>
    [Fact]
    public void TheDefaultCategoryNameIsSpelledAsExtractionReportsIt()
    {
        ElementTakeoff wall = Wall();

        Assert.True(
            CriteriaSet.Default.ByCategory.ContainsKey(wall.CategoryName),
            $"No default criterion is keyed by '{wall.CategoryName}', the category name "
                + "extraction puts on a wall, so no wall could be measured by the defaults.");
    }

    /// <summary>
    /// The default openings threshold the requirement calls for.
    /// </summary>
    /// <remarks>
    /// 1.0 m² is the value every <c>metrado-measurement</c> scenario is written
    /// against ("AND a threshold of 1.0 m²") and the one task 2.2 keeps for walls
    /// when the six-category table arrives.
    /// </remarks>
    [Fact]
    public void TheDefaultWallThresholdIsOneSquareMetre()
    {
        Assert.Equal(1.0, DefaultWalls.Threshold.Value, 9);
        Assert.Equal(QuantityUnit.SquareMetre, DefaultWalls.Threshold.Unit);
    }

    /// <summary>
    /// The threshold is expressed in the category's own measurement unit.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, requirement "Openings Threshold Is Configurable
    /// per Category": the threshold is "expressed in that category's measurement
    /// unit". This is not cosmetic — <c>Measurement.Apply</c> refuses to compare
    /// across unit systems, so a default whose threshold unit disagreed with its
    /// criterion unit would report <c>UnitMismatch</c> on a fresh installation with
    /// nothing for the user to fix.
    /// </remarks>
    [Fact]
    public void TheDefaultThresholdIsExpressedInTheCategorysOwnUnit()
    {
        Assert.Equal(DefaultWalls.Unit, DefaultWalls.Threshold.Unit);
    }

    /// <summary>
    /// The default boundary mode is the named constant.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>: "A category without an explicit mode SHALL
    /// inherit the default, which is `exclusive`." Asserted against
    /// <see cref="Defaults.Mode"/> rather than against
    /// <see cref="BoundaryMode.Exclusive"/> written out, so the criterion is pinned
    /// to the product constant and not to a second, independently drifting copy of
    /// the same decision.
    /// </remarks>
    [Fact]
    public void TheDefaultWallCriterionTakesItsBoundaryModeFromTheNamedConstant()
    {
        Assert.Equal(Defaults.Mode, DefaultWalls.Threshold.Mode);

        Assert.True(
            Enum.IsDefined(typeof(BoundaryMode), DefaultWalls.Threshold.Mode),
            $"The default criterion's mode is {(int)DefaultWalls.Threshold.Mode}, which is "
                + "not a declared BoundaryMode. The default must name a member, never an ordinal.");
    }

    /// <summary>
    /// The criterion names the Revit parameter the extraction layer reads.
    /// </summary>
    /// <remarks>
    /// The list is ordered and the order is meaningful, so it is asserted as a
    /// sequence rather than as a set. <c>HOST_AREA_COMPUTED</c> is the built-in
    /// parameter task 1.21 converts into the wall's raw area.
    /// </remarks>
    [Fact]
    public void TheDefaultWallCriterionReadsTheComputedHostArea()
    {
        Assert.Equal(new[] { "HOST_AREA_COMPUTED" }, DefaultWalls.Sources);
    }

    /// <summary>
    /// The set answers only for the categories it actually defines.
    /// </summary>
    /// <remarks>
    /// This is what makes "unknown category is rejected" possible at all: a set that
    /// manufactured a criterion for any name asked of it could never tell a
    /// supported category from a typo.
    /// </remarks>
    [Fact]
    public void ACategoryTheDefaultsDoNotDefineIsAbsentRatherThanInvented()
    {
        Assert.False(CriteriaSet.Default.ByCategory.ContainsKey("Widgets"));
        Assert.False(CriteriaSet.Default.ByCategory.TryGetValue("Widgets", out _));
    }

    /// <summary>
    /// The requirement's own scenario: "First run with no configuration".
    /// </summary>
    /// <remarks>
    /// GIVEN a fresh installation with no configuration file, WHEN the export runs
    /// over a model containing walls, THEN "walls are measured with the built-in
    /// default criterion and default threshold".
    /// <para>
    /// The wall is the <c>metrado-measurement</c> "Sub-threshold opening is added
    /// back" scenario: raw 18.0 m² with one 0.6 m² opening. Measured through the
    /// defaults it must read 18.6 m² — <em>not</em> the 18.0 m² Revit computed. A
    /// default threshold of 0, or a source key the extraction never emits, or a unit
    /// the threshold disagrees with, each produce a different number here.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFirstRunWithNoConfigurationMeasuresAWallWithTheOpeningsCorrectionApplied()
    {
        ElementTakeoff wall = WallWith(Source("HOST_AREA_COMPUTED", 18.0)) with
        {
            Openings = [new OpeningQuantity("window-1", SquareMetres(0.6))],
        };

        MetradoOutcome outcome = Measurement.Measure(wall, DefaultWalls);

        Assert.Equal(MetradoStatus.Measured, outcome.Status);
        Assert.Equal(18.6, outcome.Result!.Metrado.Value, 9);
        Assert.Equal(18.0, outcome.Result.Raw.Value, 9);
        Assert.Equal(QuantityUnit.SquareMetre, outcome.Result.Metrado.Unit);
    }

    /// <summary>
    /// Absence of configuration is not an error — it is a measurement.
    /// </summary>
    /// <remarks>
    /// The requirement's second THEN: "AND no configuration error is reported".
    /// Nothing in the defaults path returns a <see cref="ConfigError"/>, so the
    /// observable form of that claim is that a wall measured with no file produces a
    /// result and no warning.
    /// </remarks>
    [Fact]
    public void AFirstRunWithNoConfigurationReportsNothingToFix()
    {
        MetradoOutcome outcome = Measurement.Measure(
            WallWith(Source("HOST_AREA_COMPUTED", 20.0)),
            DefaultWalls);

        Assert.Equal(MetradoStatus.Measured, outcome.Status);
        Assert.Equal(20.0, outcome.Result!.Metrado.Value, 9);
        Assert.Null(outcome.Warning);
    }

    /// <summary>
    /// The built-in set is a constant, so it must not be reachable as something
    /// writable.
    /// </summary>
    /// <remarks>
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> is an interface, not a
    /// guarantee: handing out the backing <see cref="Dictionary{TKey,TValue}"/>
    /// behind it lets any caller cast it back and edit the product's defaults for
    /// every later run in the process. The cast is attempted here exactly as such a
    /// caller would write it, and the edit must be refused rather than merely
    /// discouraged.
    /// </remarks>
    [Fact]
    public void TheBuiltInDefaultsRefuseAnEditMadeThroughACastOfTheirDictionary()
    {
        CategoryCriterion replacement = new(
            "Walls",
            QuantityUnit.CubicMetre,
            ["Anything"],
            Threshold(0.0));

        if (CriteriaSet.Default.ByCategory is IDictionary<string, CategoryCriterion> mutable)
        {
            Assert.Throws<NotSupportedException>(() => mutable["Walls"] = replacement);
            Assert.Throws<NotSupportedException>(() => mutable.Remove("Walls"));
        }

        Assert.Equal(QuantityUnit.SquareMetre, DefaultWalls.Unit);
        Assert.Equal(1.0, DefaultWalls.Threshold.Value, 9);
    }

    /// <summary>
    /// Reading the defaults twice yields the same criteria.
    /// </summary>
    /// <remarks>
    /// Whether the property caches one instance or rebuilds it is an implementation
    /// detail; that two reads describe the same measurement is not. A default that
    /// differed between reads would make a run irreproducible for reasons invisible
    /// in the workbook.
    /// </remarks>
    [Fact]
    public void ReadingTheDefaultsTwiceDescribesTheSameMeasurement()
    {
        CategoryCriterion first = CriteriaSet.Default.ByCategory["Walls"];
        CategoryCriterion second = CriteriaSet.Default.ByCategory["Walls"];

        Assert.Equal(first.Unit, second.Unit);
        Assert.Equal(first.Threshold, second.Threshold);
        Assert.Equal(first.Sources, second.Sources);
    }
}
