using ClosedXML.Excel;
using Metrado.Configuration;
using Metrado.Domain;
using Metrado.Excel;

namespace Metrado.Integration.Tests;

/// <summary>
/// The whole export, composed: resolve the criteria, measure every element under
/// the criterion for its category, codify it, group the lines, report the run and
/// write the workbook.
/// </summary>
/// <remarks>
/// <b>This composition is test-side, and that is a known gap rather than a
/// preference.</b> The shipped composition is <c>ExportTakeoffCommand</c>, task
/// 1.24, which is <c>[win]</c> — it lives in <c>Metrado.Revit2027</c> and cannot be
/// built on macOS. So this suite proves that the six stages <em>can</em> compose
/// into a correct budget; it does not prove that the command composes them this
/// way. If 1.24 wires them differently, these tests keep passing over a pipeline
/// nobody ships.
/// <para>
/// The mitigation is direction, not duplication: 1.24 should call the stages in
/// this order and nothing else, and this file is the reference for what that order
/// is. Turning it into production code on the macOS side would mean inventing an
/// orchestration layer the design does not have — the design puts the wiring in the
/// Revit command, and the seam that makes it testable is the DTO, not a second
/// orchestrator.
/// </para>
/// </remarks>
internal static class ExportPipeline
{
    /// <summary>
    /// Runs the export the way an estimator with no configuration file does.
    /// </summary>
    /// <remarks>
    /// Starts one stage earlier than <see cref="Run"/>, at
    /// <see cref="CriteriaResolver"/>, so the criteria the rest of the pipeline
    /// measures under are the ones resolution actually produced. Constructing an
    /// <see cref="EffectiveCriteria"/> by hand here would leave the first stage
    /// untested and the built-in defaults asserted against themselves.
    /// </remarks>
    internal static ExportRun RunWithBuiltInDefaults(IReadOnlyList<ElementTakeoff> model)
    {
        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(CriteriaFileLookup.Absent, path: null);

        Assert.True(
            resolved.IsOk,
            "Resolving with no criteria file present must yield the built-in defaults. "
                + $"It failed instead: {(resolved.IsOk ? string.Empty : resolved.Error.Message)}");

        return Run(resolved.Value, model);
    }

    /// <summary>
    /// Runs the export under criteria the caller supplies.
    /// </summary>
    /// <param name="criteria">
    /// The criteria in force. Every threshold, mode and unit the measurement stage
    /// uses is read out of here and out of nothing else, which is what makes a
    /// pipeline that quietly reached for <see cref="CriteriaSet.Default"/> instead
    /// detectable by a test that supplies something other than the defaults.
    /// </param>
    internal static ExportRun Run(EffectiveCriteria criteria, IReadOnlyList<ElementTakeoff> model)
    {
        CodificationChain chain = CodificationChain.Standard(sharedParameter: null);

        List<Linea> lineas = [];
        List<ValidationWarning> warnings = [];

        foreach (ElementTakeoff element in model)
        {
            MetradoOutcome outcome = Measurement.Measure(element, CriterionFor(criteria, element));

            if (outcome.Warning is not null)
            {
                warnings.Add(outcome.Warning);
            }

            lineas.Add(new Linea(element, chain.Resolve(element), Measured(element, outcome)));
        }

        TakeoffResult result = TakeoffResult.Group(lineas);

        return new ExportRun(criteria, result, RunReport.For(result, warnings), Written(result));
    }

    /// <summary>The criterion the criteria in force define for this element's category.</summary>
    /// <exception cref="InvalidOperationException">
    /// The category has no criterion. I1 measures Walls only, so this is a model the
    /// test described wrongly rather than a condition the pipeline must survive —
    /// I2's "a supported-category element with no applicable criterion" warning
    /// (task 3.3) is what handles it for real.
    /// </exception>
    private static CategoryCriterion CriterionFor(EffectiveCriteria criteria, ElementTakeoff element)
    {
        if (criteria.Criteria.ByCategory.TryGetValue(element.CategoryName, out CategoryCriterion? criterion))
        {
            return criterion;
        }

        throw new InvalidOperationException(
            $"The criteria in force define no criterion for '{element.CategoryName}'. "
                + $"Configured categories: {string.Join(", ", criteria.Criteria.ByCategory.Keys)}.");
    }

    /// <summary>
    /// Unwraps a measurement this suite's model is supposed to produce.
    /// </summary>
    /// <remarks>
    /// Every element in the fixture is measurable, so a refusal here means the
    /// model was described wrongly and the assertions downstream would be reading a
    /// budget with a wall silently missing from it. Failing at the refusal names the
    /// element; failing later would only show a total that is quietly too small.
    /// <para>
    /// A test that needs the <c>NoSource</c> or <c>UnitMismatch</c> path adds the
    /// branch it needs along with itself. Writing those branches now would be
    /// speculation no assertion covers.
    /// </para>
    /// </remarks>
    private static MetradoResult Measured(ElementTakeoff element, MetradoOutcome outcome)
    {
        if (outcome.Status is MetradoStatus.Measured or MetradoStatus.Counted && outcome.Result is not null)
        {
            return outcome.Result;
        }

        throw new InvalidOperationException(
            $"Element '{element.UniqueId}' was not measured ({outcome.Status}): "
                + $"{outcome.Warning?.Condition ?? "no warning was raised"}.");
    }

    /// <summary>
    /// The workbook, written out and read back from the bytes that were produced.
    /// </summary>
    /// <remarks>
    /// Round-tripped through a <see cref="MemoryStream"/> rather than inspected in
    /// memory, matching the writer's own suite: a workbook that is arranged
    /// correctly in memory and fails to persist ships an empty budget, and only
    /// reopening the bytes can tell the two apart. Nothing touches the file system,
    /// so a run leaves no temporary file behind to clean up.
    /// </remarks>
    private static XLWorkbook Written(TakeoffResult result)
    {
        MemoryStream stream = new();
        TakeoffWorkbook.Write(result, stream);
        stream.Position = 0;

        return new XLWorkbook(stream);
    }
}
