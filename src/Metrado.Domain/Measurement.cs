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
    /// <remarks>
    /// Unit agreement between <paramref name="raw"/>, <paramref name="openings"/>
    /// and <paramref name="threshold"/> is still a precondition here rather than
    /// an enforced one; refusing a mismatched comparison is
    /// <c>MetradoStatus.UnitMismatch</c>, which lands next. Nothing calls this
    /// method yet — source selection, grouping and extraction all come later — so
    /// no mismatched quantity can reach it before that check exists.
    /// </remarks>
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
