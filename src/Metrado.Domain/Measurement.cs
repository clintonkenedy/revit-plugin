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
    /// <exception cref="ArgumentException"><paramref name="openings"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="threshold"/> carries a boundary mode that is not a declared
    /// member.
    /// </exception>
    /// <remarks>
    /// Unit agreement between <paramref name="raw"/>, <paramref name="openings"/>
    /// and <paramref name="threshold"/> is a precondition here, not yet an
    /// enforced one: refusing a mismatched comparison is task 1.8's
    /// <c>MetradoStatus.UnitMismatch</c>. Nothing calls this method yet — source
    /// selection, grouping and extraction all land later — so no mismatched
    /// quantity can reach it before that check exists.
    /// </remarks>
    public static MetradoResult Apply(
        Quantity raw,
        IReadOnlyList<Quantity> openings,
        OpeningsThreshold threshold)
    {
        Guard.RequiredValue(openings, nameof(openings));

        // Checked before the loop rather than inside it. An element with no
        // openings would otherwise skip the check entirely and return a result
        // stamped with a convention the product cannot name.
        RequireDeclaredMode(threshold);

        double addedBack = 0;

        foreach (Quantity opening in openings)
        {
            if (IsNotDeducted(opening, threshold))
            {
                addedBack += opening.Value;
            }
        }

        return new MetradoResult(
            new Quantity(raw.Value + addedBack, raw.Unit),
            raw,
            threshold.Mode,
            threshold.Value);
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
