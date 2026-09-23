using System.Globalization;

namespace Metrado.Domain;

/// <summary>
/// The pre-export pass (task 3.3): every element measured under its
/// category's criterion and coded, and the run's validation warnings, each
/// naming its element (UniqueId, category, family, type) and the condition.
/// It runs before the workbook is written, and a warning never stops it: an
/// element that cannot be measured is reported and the rest go on.
/// </summary>
/// <remarks>
/// The conditions it raises: an element whose category has no criterion; a
/// criterion none of whose sources had a value, or whose units disagree; an
/// element whose openings exceed its gross quantity; a layered element whose
/// materials do not account for it; an opening near the threshold; a layer
/// measured as its category's material. The export composes this pass with
/// grouping and the run report, and the integration suite does the same, so
/// both run the one composition.
/// </remarks>
public static class TakeoffPass
{
    /// <summary>
    /// How close to its category's threshold an outline-measured opening must
    /// be to be flagged as possibly misclassified. The host probe found
    /// measured outlines within 1e-4 m2 of Revit's deduction; this leaves a
    /// hundredfold margin. The numbers are never changed, only flagged.
    /// </summary>
    public const double ThresholdBandSquareMetres = 0.01;

    /// <summary>The measured and coded lines, and every warning the pass raised, in element order.</summary>
    public sealed record Outcome(IReadOnlyList<Linea> Lines, IReadOnlyList<ValidationWarning> Warnings);

    public static Outcome Run(CriteriaSet criteria, IReadOnlyList<ElementTakeoff> elements, CodificationChain chain)
    {
        Guard.RequiredValue(criteria, nameof(criteria));
        Guard.RequiredValue(elements, nameof(elements));
        Guard.RequiredValue(chain, nameof(chain));

        List<Linea> lineas = [];
        List<ValidationWarning> warnings = [];
        HashSet<string> notedTypes = new(StringComparer.Ordinal);

        foreach (ElementTakeoff element in elements)
        {
            if (!criteria.ByCategory.TryGetValue(element.CategoryName, out CategoryCriterion? criterion))
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

        return new Outcome(lineas, warnings);
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
                named.Count == 1 ? $"its layer {named[0]}" : $"its layers {string.Join(", ", named.Take(named.Count - 1))} and {named[named.Count - 1]}",
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
