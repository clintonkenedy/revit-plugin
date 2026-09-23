using System.Globalization;
using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// The export's composition as the command runs it: measure every element
/// under its category's criterion, codify it, group the lines and report the
/// run. Uses no Revit type.
///
/// It follows the stage order of the integration suite's test-side
/// <c>ExportPipeline</c> and departs from it only where a real model demands:
/// an element that cannot be measured is reported and the run goes on, and
/// the warnings extraction raised reach the report.
/// </summary>
public static class TakeoffExport
{
    /// <summary>
    /// How close to its category's threshold an outline-measured opening must
    /// be to be flagged as possibly misclassified. The host probe found
    /// measured outlines within 1e-4 m2 of Revit's deduction; this leaves a
    /// hundredfold margin. The numbers are never changed, only flagged.
    /// </summary>
    public const double ThresholdBandSquareMetres = 0.01;

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
        CodificationChain chain = CodificationChain.Standard(sharedParameter: null);
        List<Linea> lineas = [];
        List<ValidationWarning> warnings = [.. extractionWarnings];

        foreach (ElementTakeoff element in elements)
        {
            if (!criteria.Criteria.ByCategory.TryGetValue(element.CategoryName, out CategoryCriterion? criterion))
            {
                warnings.Add(ValidationWarning.ForElement(
                    element,
                    $"No criterion is configured for category '{element.CategoryName}', so it was not measured."));
                continue;
            }

            MetradoOutcome outcome = Measurement.Measure(element, criterion);
            if (outcome.Warning is not null)
            {
                warnings.Add(outcome.Warning);
            }

            // Not measured (no source had a value, or units disagreed): its
            // warning above says why, and no line is invented for it.
            if (outcome is { Status: MetradoStatus.Measured or MetradoStatus.Counted, Result: MetradoResult result })
            {
                lineas.Add(new Linea(element, chain.Resolve(element), result));

                // Only a measured host (wall, floor, roof) had its openings decided by the rule.
                warnings.AddRange(NearThreshold(element, criterion.Threshold));
            }
        }

        TakeoffResult grouped = TakeoffResult.Group(lineas);
        return new Outcome(grouped, RunReport.For(grouped, warnings));
    }

    private static IEnumerable<ValidationWarning> NearThreshold(ElementTakeoff element, OpeningsThreshold threshold) =>
        element.Openings
            .Where(opening => opening.Amount.Unit == threshold.Unit
                && Math.Abs(opening.Amount.Value - threshold.Value) <= ThresholdBandSquareMetres)
            .Select(opening => ValidationWarning.ForElement(
                element,
                // The value in full: rounded, 0.99997 and 1.0 both read "1",
                // yet the rule decides them oppositely.
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Opening {0} measures {1:0.#########} {2}, within {3} of the {4} {2} threshold ({5}), and the rule {6}. "
                    + "Its area is measured by its outline, which can be off by that much, so check it is on the right side.",
                    opening.UniqueId,
                    opening.Amount.Value,
                    threshold.Unit.Symbol(),
                    ThresholdBandSquareMetres,
                    threshold.Value,
                    threshold.Mode == BoundaryMode.Inclusive ? "inclusive" : "exclusive",
                    Measurement.IsAddedBack(opening.Amount, threshold) ? "added it back" : "kept it deducted")));
}
