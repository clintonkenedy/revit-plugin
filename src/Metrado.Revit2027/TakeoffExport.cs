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
        HashSet<string> notedTypes = new(StringComparer.Ordinal);

        foreach (ElementTakeoff element in elements)
        {
            if (!criteria.Criteria.ByCategory.TryGetValue(element.CategoryName, out CategoryCriterion? criterion))
            {
                warnings.Add(ValidationWarning.ForElement(
                    element,
                    $"No criterion is configured for category '{element.CategoryName}', so it was not measured."));
                continue;
            }

            // A category taken off by layer: its materials' lines, unless they
            // do not account for all of it, when it is measured whole below,
            // under its own code, its reason already reported.
            if (criterion.Layers is not null && ByLayer(element, criterion, chain, lineas, warnings, notedTypes))
            {
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

    /// <summary>
    /// Takes a layered element off by its materials, each line coded by its
    /// material (task 3.2); false when it must be measured whole, its reason
    /// already among the warnings.
    /// </summary>
    private static bool ByLayer(
        ElementTakeoff element,
        CategoryCriterion criterion,
        CodificationChain chain,
        List<Linea> lineas,
        List<ValidationWarning> warnings,
        HashSet<string> notedTypes)
    {
        IReadOnlyList<LayerLine>? lines = LayerMeasurement.Measure(element, criterion).Match<IReadOnlyList<LayerLine>?>(
            byLayer: (measured, raised) =>
            {
                warnings.AddRange(raised);
                return measured;
            },
            whole: why =>
            {
                warnings.Add(why);
                return null;
            });
        if (lines is null)
        {
            return false;
        }

        lineas.AddRange(lines.Select(line => new Linea(element, chain.ResolveLayer(element, line.Layer.Material), line.Metrado, line.Layer)));

        // Every element of a type has its layers, so the note is said once for the type.
        if (CategoryMaterialNote(element, lines) is ValidationWarning note && notedTypes.Add(element.TypeKey))
        {
            warnings.Add(note);
        }

        // Each opening was decided once, at the host's threshold, so it is flagged once.
        warnings.AddRange(NearThreshold(element, criterion.Threshold));
        return true;
    }

    /// <summary>
    /// A layer its type gives no material of its own is measured as the
    /// category's material, which then codes and prices it as that. Null when
    /// every layer has its own.
    /// </summary>
    private static ValidationWarning? CategoryMaterialNote(ElementTakeoff element, IReadOnlyList<LayerLine> lines)
    {
        List<(CompoundLayer Layer, string Material)> borrowed =
        [
            .. lines
                .SelectMany(line => line.Layer.Layers.Where(layer => layer.MaterialFromCategory).Select(layer => (layer, line.Layer.Material.MaterialName)))
                .OrderBy(entry => entry.layer.Position),
        ];
        if (borrowed.Count == 0)
        {
            return null;
        }

        // Counted from 1 among the layers alone: Revit's Edit Assembly also
        // numbers its core boundaries, so its row numbers are not these.
        List<string> named =
        [
            .. borrowed.Select(entry => string.Format(CultureInfo.InvariantCulture, "{0} ({1}, {2:0.###} mm)", entry.Layer.Position + 1, entry.Layer.Function, entry.Layer.Width.Value * 1000)),
        ];
        return ValidationWarning.ForElement(
            element,
            string.Format(
                CultureInfo.InvariantCulture,
                "Its type gives {0} from the exterior (or top) no material of {1}, so {2} measured as the category's material, {3}. "
                    + "Assign {4} one in the type, so {5} coded and priced as what it is. Said once for the type.",
                named.Count == 1 ? $"its layer {named[0]}" : $"its layers {string.Join(", ", named.Take(named.Count - 1))} and {named[^1]}",
                borrowed.Count == 1 ? "its own" : "their own",
                borrowed.Count == 1 ? "it is" : "they are",
                string.Join(", ", borrowed.Select(entry => entry.Material).Distinct(StringComparer.Ordinal)),
                borrowed.Count == 1 ? "it" : "each",
                borrowed.Count == 1 ? "it is" : "each is"));
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
