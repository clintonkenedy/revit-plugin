using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// The export's composition as the command runs it: the takeoff pass, which
/// measures, codes and validates every element (task 3.3), then grouping and
/// the run report. Uses no Revit type.
///
/// The integration suite's test-side <c>ExportPipeline</c> runs the same pass,
/// so the two compose the one measurement; this adds only what the command
/// has and a test model does not, the warnings extraction raised.
/// </summary>
public static class TakeoffExport
{
    public sealed record Outcome(TakeoffResult Result, RunReport Report);

    public static Outcome Run(
        EffectiveCriteria criteria,
        IReadOnlyList<ElementTakeoff> elements,
        IReadOnlyList<ValidationWarning> extractionWarnings)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(extractionWarnings);

        // No shared parameter can be nominated yet; the chain reads Assembly Code, then Keynote.
        TakeoffPass.Outcome pass = TakeoffPass.Run(criteria.Criteria, elements, CodificationChain.Standard(sharedParameter: null));
        TakeoffResult grouped = TakeoffResult.Group(pass.Lines);
        return new Outcome(grouped, RunReport.For(grouped, [.. extractionWarnings, .. pass.Warnings]));
    }
}
