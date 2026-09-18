using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// The corrected metrado survives every stage between the criteria and the
/// budget: resolution, measurement, codification, grouping and the run report.
/// </summary>
/// <remarks>
/// The openings correction is already proved as a rule by
/// <c>MeasurementApplyTests</c>. What is <em>not</em> proved there is that the
/// corrected number survives the composition — five stages, any one of which could
/// substitute the raw quantity and leave every unit test green. That substitution
/// is the specific failure <c>metrado-measurement</c> names in its purpose:
/// "exporting it unchanged ships a wrong budget that looks plausible".
/// <para>
/// Each assertion states both numbers rather than only their inequality. "They
/// differ" is also satisfied by a stage that corrupts the value, so a bare
/// <c>NotEqual</c> would pass for the failure these tests are meant to catch.
/// </para>
/// </remarks>
public sealed class MeasuredModelComposesIntoABudgetTests
{
    /// <summary>
    /// <c>metrado-measurement</c>, "Sub-threshold opening is added back": 18.0 m²
    /// with a 0.6 m² opening measures 18.6 m² under the built-in 1.0 m² threshold.
    /// </summary>
    [Fact]
    public void AtLeastOneMeasuredMetradoDiffersFromTheRawRevitArea()
    {
        ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        MetradoResult measured = run.LineaOf(ModelFixture.CorrectedByOneOpening).Metrado;

        Assert.Equal(18.0, ModelFixture.RawAreaOf(ModelFixture.CorrectedByOneOpening), 9);
        Assert.Equal(18.0, measured.Raw.Value, 9);
        Assert.Equal(18.6, measured.Metrado.Value, 9);
        Assert.NotEqual(measured.Raw.Value, measured.Metrado.Value);
    }
}
