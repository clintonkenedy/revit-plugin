using System.Globalization;

namespace Metrado.Domain;

/// <summary>
/// The measurement rule: turning a raw Revit quantity into a metrado.
/// </summary>
/// <remarks>
/// Pure and static. Every input is a validated value object, so the rule holds no
/// state, reads no configuration and performs no I/O — which is what lets the
/// single most important behaviour in the product be tested on macOS with no
/// Revit installed.
/// </remarks>
public static class Measurement
{
    /// <summary>
    /// Reads the criterion's quantity sources in order and selects the first one
    /// that has a value for this element.
    /// </summary>
    /// <remarks>
    /// The outer loop is the criterion's source list, never the element's
    /// quantities: priority is a property of the configured criterion, and
    /// iterating the element instead would let whatever order Revit returned the
    /// parameters in decide which one is measured.
    /// <para>
    /// "Has a value" means the source key is present among the element's
    /// quantities. A source present and reading <c>0.0</c> has a value and wins —
    /// falling through to the next source would silently report a genuinely empty
    /// element as some other parameter's number.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The quantity the winning source carried, or <see cref="SourceSelection.None"/>
    /// when no listed source had one. Never a zero standing in for the absence.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="element"/> or <paramref name="criterion"/> is null.
    /// </exception>
    public static SourceSelection SelectSource(
        ElementTakeoff element,
        CategoryCriterion criterion)
    {
        Guard.RequiredValue(element, nameof(element));
        Guard.RequiredValue(criterion, nameof(criterion));

        foreach (string sourceKey in criterion.Sources)
        {
            foreach (RawQuantity quantity in element.Quantities)
            {
                // A populated material means this quantity describes one layer, not
                // the element. Measuring a wall by its insulation is a different
                // fact wearing a correct-looking number.
                if (quantity.Material is null
                    && string.Equals(quantity.SourceKey, sourceKey, StringComparison.Ordinal))
                {
                    return SourceSelection.Of(quantity.Amount);
                }
            }
        }

        return SourceSelection.None;
    }

    /// <summary>
    /// Measures one element under its category's criterion: reads the criterion's
    /// quantity sources, then applies the openings correction to whichever one had
    /// a value.
    /// </summary>
    /// <remarks>
    /// The only composition of <see cref="SelectSource"/> and <see cref="Apply"/>,
    /// and the reason the correction cannot run on an element that was never
    /// measured: the selection hands its quantity to a function it invokes only
    /// when one exists, so the <c>none</c> branch has no <see cref="Quantity"/> to
    /// pass on even if it wanted to.
    /// <para>
    /// A criterion listing no sources at all is a counted category (N1): it is
    /// decided first, before any source is looked for, and reports
    /// <see cref="MetradoStatus.Counted"/>, so it is never reported as a category
    /// whose sources all came up empty.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="element"/> or <paramref name="criterion"/> is null.
    /// </exception>
    public static MetradoOutcome Measure(ElementTakeoff element, CategoryCriterion criterion)
    {
        Guard.RequiredValue(element, nameof(element));
        Guard.RequiredValue(criterion, nameof(criterion));

        // N1: no source listed is a counted category, decided before any source
        // is looked for, so it is never taken for sources that came up empty.
        if (criterion.Sources.Count == 0)
        {
            Quantity one = new(1, criterion.Unit);
            return new MetradoOutcome(
                MetradoStatus.Counted,
                new MetradoResult(one, one, one, criterion.Threshold.Mode, criterion.Threshold.Value, ClampedToGross: false),
                Warning: null);
        }

        return SelectSource(element, criterion).Match(
            selected: raw => Apply(element, raw, OpeningAmounts(element), criterion.Threshold),
            none: () => new MetradoOutcome(
                MetradoStatus.NoSource,
                Result: null,
                NoSourceHadAValue(element, criterion)));
    }

    /// <summary>
    /// The opening amounts, in the order the element carries them, still one by one.
    /// </summary>
    private static IReadOnlyList<Quantity> OpeningAmounts(ElementTakeoff element)
    {
        Quantity[] amounts = new Quantity[element.Openings.Count];

        for (int i = 0; i < amounts.Length; i++)
        {
            amounts[i] = element.Openings[i].Amount;
        }

        return amounts;
    }

    /// <summary>
    /// Reports an element the criterion could not measure at all.
    /// </summary>
    /// <remarks>
    /// The sources are listed in the message because the user's fix is to populate
    /// one of them, and naming them is the difference between a warning they can
    /// act on and a warning they can only acknowledge.
    /// </remarks>
    private static ValidationWarning NoSourceHadAValue(
        ElementTakeoff element,
        CategoryCriterion criterion) =>
        ValidationWarning.ForElement(
            element,
            $"No quantity source had a value, so this element has no metrado and was "
                + $"not measured as zero. The {criterion.Category} criterion reads "
                + $"{string.Join(", ", criterion.Sources)} in that order.");

    /// <summary>
    /// Applies the openings correction, turning a Revit-computed quantity into a
    /// metrado:
    /// <code>
    /// metrado = rawQuantity + Σ q(o) for every opening o where q(o) ≺ threshold
    /// </code>
    /// where <c>≺</c> is the threshold's <see cref="BoundaryMode"/>.
    /// </summary>
    /// <param name="element">
    /// The element being measured, carried for identity alone: every validation
    /// warning this rule can raise has to name the element the user must go and
    /// fix, and <c>UniqueId</c> is the only handle that survives the Revit seam.
    /// </param>
    /// <param name="raw">
    /// The Revit-computed quantity. Revit's <c>HOST_AREA_COMPUTED</c> and
    /// <c>HOST_VOLUME_COMPUTED</c> have <em>already subtracted every opening</em>,
    /// which is precisely why this correction exists: metrado norms do not deduct
    /// small openings, so exporting the raw parameter ships a wrong budget that
    /// looks entirely plausible.
    /// </param>
    /// <param name="openings">
    /// The individual opening quantities. Never pre-aggregated: the rule compares
    /// each opening against the threshold on its own, and comparing their sum
    /// instead is a different rule that produces a different, wrong number.
    /// </param>
    /// <param name="threshold">
    /// The size below which an opening is added back, plus the convention applied
    /// at exact equality. Valid by construction, so this method needs no
    /// validation branch for its value.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="element"/> or <paramref name="openings"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="threshold"/> carries a boundary mode that is not a declared
    /// member.
    /// </exception>
    public static MetradoOutcome Apply(
        ElementTakeoff element,
        Quantity raw,
        IReadOnlyList<Quantity> openings,
        OpeningsThreshold threshold)
    {
        Guard.RequiredValue(element, nameof(element));
        Guard.RequiredValue(openings, nameof(openings));

        // Checked before the loop rather than inside it. An element with no
        // openings would otherwise skip the check entirely and return a result
        // stamped with a convention the product cannot name.
        RequireDeclaredMode(threshold);

        // Before any arithmetic. The comparison against the threshold is exactly
        // what has to be refused, so discovering the mismatch after running it
        // would mean reporting a number the rule had already decided it could not
        // compute.
        if (MismatchedUnit(raw, openings, threshold) is QuantityUnit foreign)
        {
            return new MetradoOutcome(
                MetradoStatus.UnitMismatch,
                Result: null,
                UnitsDisagree(element, foreign, threshold));
        }

        return Correct(element, raw, openings, threshold, factor: 1);
    }

    /// <summary>
    /// The correction's arithmetic, once units are known to agree: every
    /// opening decided against the threshold on its own, and what it adds
    /// back, and to the gross, scaled by <paramref name="factor"/>. A whole
    /// element's factor is 1; a material layer's is its share of each opening
    /// (task 3.2), or 0 when that share cannot be trusted.
    /// </summary>
    internal static MetradoOutcome Correct(
        ElementTakeoff element,
        Quantity raw,
        IReadOnlyList<Quantity> openings,
        OpeningsThreshold threshold,
        double factor)
    {
        double addedBack = 0;
        double allOpenings = 0;

        // One pass produces both sums. Enumerating the list twice would let an
        // openings collection that changes between the passes report a gross
        // bound that was never the bound the metrado was computed against.
        foreach (Quantity opening in openings)
        {
            allOpenings += opening.Value;

            if (IsNotDeducted(opening, threshold))
            {
                addedBack += opening.Value;
            }
        }

        addedBack *= factor;
        allOpenings *= factor;
        double corrected = raw.Value + addedBack;
        double gross = raw.Value + allOpenings;

        // Written as positive `<=` comparisons on purpose. Negating them instead
        // would make a NaN on either side satisfy the bound silently, which is how
        // a broken parameter read reaches the workbook as a measured figure.
        bool boundHolds = corrected <= gross && allOpenings <= gross;

        MetradoResult result = new(
            new Quantity(boundHolds ? corrected : Math.Min(corrected, gross), raw.Unit),
            raw,
            new Quantity(gross, raw.Unit),
            threshold.Mode,
            threshold.Value,
            ClampedToGross: !boundHolds);

        return new MetradoOutcome(
            MetradoStatus.Measured,
            result,
            boundHolds ? null : GrossBoundViolated(element, result, allOpenings));
    }

    /// <summary>
    /// The first unit that does not match the threshold's, or <c>null</c> when
    /// every quantity agrees with it.
    /// </summary>
    /// <remarks>
    /// The raw quantity is checked first because it is the one the metrado is
    /// built from, and the openings are checked individually for the same reason
    /// the correction evaluates them individually: one opening read in the wrong
    /// unit is enough to corrupt the total, and a whole-list check would have to
    /// decide which unit the list "really" is.
    /// </remarks>
    private static QuantityUnit? MismatchedUnit(
        Quantity raw,
        IReadOnlyList<Quantity> openings,
        OpeningsThreshold threshold)
    {
        if (raw.Unit != threshold.Unit)
        {
            return raw.Unit;
        }

        foreach (Quantity opening in openings)
        {
            if (opening.Unit != threshold.Unit)
            {
                return opening.Unit;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports a quantity that cannot be compared against the threshold.
    /// </summary>
    /// <remarks>
    /// This is an adapter fault, not a modelling mistake, so the message names
    /// both units rather than asking the user to change the model. Every quantity
    /// crossing the Revit seam declares its unit precisely so this is detectable
    /// instead of silent.
    /// </remarks>
    private static ValidationWarning UnitsDisagree(
        ElementTakeoff element,
        QuantityUnit foreign,
        OpeningsThreshold threshold) =>
        ValidationWarning.ForElement(
            element,
            $"Measured in {Describe(foreign)} but the openings threshold is in "
                + $"{Describe(threshold.Unit)}. The comparison was refused rather "
                + "than converted, so this element has no metrado.");

    /// <summary>
    /// Names a unit for a user-facing message, without throwing on one that was
    /// never declared — an undeclared unit is precisely what such a message is
    /// most likely to be reporting.
    /// </summary>
    private static string Describe(QuantityUnit unit) =>
        Enum.IsDefined(typeof(QuantityUnit), unit) ? unit.Symbol() : "an undeclared unit";

    /// <summary>
    /// Reports extraction input that contradicts its own gross quantity.
    /// </summary>
    /// <remarks>
    /// The correction restores material Revit deducted, so it can reach the gross
    /// quantity but never pass it. Input that breaks the bound is not a metrado
    /// that needs rounding — it is extraction that cannot be trusted, and the
    /// specification is explicit that the system reports it rather than exporting
    /// an impossible figure.
    /// <para>
    /// The second half of the bound, <c>Σ q(o) ≤ gross</c>, reduces to a raw
    /// quantity that is not negative. It is written in the specification's own
    /// terms because that is the condition the user is told about; Revit computing
    /// a negative area is the extraction fault it detects.
    /// </para>
    /// </remarks>
    private static ValidationWarning GrossBoundViolated(
        ElementTakeoff element,
        MetradoResult result,
        double allOpenings) =>
        ValidationWarning.ForElement(
            element,
            $"Metrado clamped to the gross quantity {Format(result.Gross.Value)} "
                + $"{result.Gross.Unit.Symbol()}: the raw quantity "
                + $"{Format(result.Raw.Value)} and its openings totalling "
                + $"{Format(allOpenings)} are inconsistent, so the corrected value "
                + "would not be a quantity this element can have.");

    /// <summary>
    /// Formats a quantity for a user-facing message. Invariant on purpose: the
    /// host's locale decides whether an unqualified <c>ToString</c> writes a
    /// decimal comma, and a warning must read the same for every user.
    /// </summary>
    private static string Format(double value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether the rule adds this opening back — the decision <see cref="Apply"/>
    /// makes for it, exposed so a report can say which way the rule went
    /// without restating the rule.
    /// </summary>
    /// <exception cref="ArgumentException">The opening is not in the threshold's unit.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The threshold's mode is not a declared member.</exception>
    public static bool IsAddedBack(Quantity opening, OpeningsThreshold threshold)
    {
        RequireDeclaredMode(threshold);
        if (opening.Unit != threshold.Unit)
        {
            throw new ArgumentException(
                $"The opening is in {opening.Unit} and the threshold in {threshold.Unit}; the rule never compares across units.",
                nameof(opening));
        }

        return IsNotDeducted(opening, threshold);
    }

    /// <summary>
    /// Whether the norm keeps this opening's material in the metrado — that is,
    /// whether Revit's deduction of it must be added back.
    /// </summary>
    private static bool IsNotDeducted(Quantity opening, OpeningsThreshold threshold) =>
        threshold.Mode switch
        {
            // "no se descuentan los vanos de área menor a X"
            BoundaryMode.Exclusive => opening.Value < threshold.Value,

            // "no se descuentan los vanos hasta X"
            BoundaryMode.Inclusive => opening.Value <= threshold.Value,

            // Unreachable: RequireDeclaredMode ran first. Kept so the comparison
            // cannot silently acquire a default arm if that guard is ever moved.
            _ => throw UndeclaredMode(threshold),
        };

    /// <summary>
    /// Refuses a threshold whose mode was never configured.
    /// </summary>
    /// <remarks>
    /// <c>OpeningsThreshold.TryCreate</c> rejects an undeclared mode, but every
    /// struct in C# has a reachable zero value, so <c>default(OpeningsThreshold)</c>
    /// arrives here with one. Its value is 0, which disables the correction, so the
    /// arithmetic would be safe — the refusal protects the <em>report</em>, not the
    /// number. A result stamped with mode <c>0</c> claims a convention that cannot
    /// be named, and that claim is written into the workbook.
    /// </remarks>
    private static void RequireDeclaredMode(OpeningsThreshold threshold)
    {
        if (!Enum.IsDefined(typeof(BoundaryMode), threshold.Mode))
        {
            throw UndeclaredMode(threshold);
        }
    }

    private static ArgumentOutOfRangeException UndeclaredMode(OpeningsThreshold threshold) =>
        new(
            nameof(threshold),
            threshold.Mode,
            $"Not a declared boundary mode. Expected {nameof(BoundaryMode.Exclusive)} "
                + $"or {nameof(BoundaryMode.Inclusive)}.");
}
