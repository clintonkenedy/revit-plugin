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
/// The measurement is the shipped one: Domain's <see cref="TakeoffPass"/>,
/// which the command's <c>TakeoffExport</c> runs too, measures, codes and
/// validates every element. What stays test-side is only the composition
/// around it, grouping, the run report and the writer, which the command
/// cannot share with a macOS suite because it lives in <c>Metrado.Revit2027</c>;
/// it is three calls, in the same order as the command's.
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
        TakeoffPass.Outcome pass = TakeoffPass.Run(criteria.Criteria, model, CodificationChain.Standard(criteria.SharedParameter?.ToString("D")));
        RequireEveryElementMeasured(model, pass);

        TakeoffResult result = TakeoffResult.Group(pass.Lines);

        return new ExportRun(criteria, result, RunReport.For(result, pass.Warnings), Written(result));
    }

    /// <summary>
    /// Fails on an element of the model that gave no line.
    /// </summary>
    /// <remarks>
    /// Every element in the fixtures is measurable, so one with no line means
    /// the model was described wrongly, and the assertions downstream would be
    /// reading a budget with a wall silently missing from it. Failing here names
    /// the element and why; failing later would only show a total that is
    /// quietly too small.
    /// </remarks>
    private static void RequireEveryElementMeasured(IReadOnlyList<ElementTakeoff> model, TakeoffPass.Outcome pass)
    {
        if (model.FirstOrDefault(element => !pass.Lines.Any(line => line.Element.UniqueId == element.UniqueId)) is ElementTakeoff missing)
        {
            throw new InvalidOperationException(
                $"Element '{missing.UniqueId}' was not measured: "
                    + string.Join(" ", pass.Warnings.Where(warning => warning.UniqueId == missing.UniqueId).Select(warning => warning.Condition)));
        }
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
