using System.Globalization;

namespace Metrado.Domain;

/// <summary>
/// The openings threshold for one category: the size below which an opening is
/// added back into the metrado, the unit that size is expressed in, and the
/// boundary convention applied at equality.
/// </summary>
/// <remarks>
/// Valid by construction (decision 6), so the measurement rule needs no
/// validation branch of its own. <see cref="TryCreate"/> is the only door in.
/// <para>
/// The unit travels with the threshold because the comparison must happen in one
/// consistent unit system; Domain never converts, so a threshold whose unit
/// differs from the quantities being compared is refused by the rule rather than
/// reconciled.
/// </para>
/// </remarks>
public readonly record struct OpeningsThreshold
{
    private OpeningsThreshold(double value, QuantityUnit unit, BoundaryMode mode)
    {
        Value = value;
        Unit = unit;
        Mode = mode;
    }

    public double Value { get; }

    public QuantityUnit Unit { get; }

    public BoundaryMode Mode { get; }

    /// <summary>
    /// Builds a threshold, or explains why the configuration is invalid.
    /// </summary>
    /// <param name="category">
    /// The category this threshold belongs to, named in the error so the user can
    /// find the entry to fix. Optional because a threshold can be built before a
    /// category is known; every configuration path supplies it.
    /// </param>
    public static Result<OpeningsThreshold, ConfigError> TryCreate(
        double value,
        QuantityUnit unit,
        BoundaryMode mode,
        string? category = null)
    {
        // NaN first: every comparison against NaN is false, so `value < 0` would
        // wave it through and the rule would then silently deduct every opening.
        if (double.IsNaN(value))
        {
            return Invalid("Openings threshold must be a number.", category, "NaN");
        }

        if (value < 0)
        {
            return Invalid(
                "Openings threshold must not be negative.",
                category,
                value.ToString(CultureInfo.InvariantCulture));
        }

        if (!Enum.IsDefined(typeof(BoundaryMode), mode))
        {
            return Invalid(
                $"Openings boundary mode must be {nameof(BoundaryMode.Exclusive)} or "
                    + $"{nameof(BoundaryMode.Inclusive)}.",
                category,
                ((int)mode).ToString(CultureInfo.InvariantCulture));
        }

        return Result<OpeningsThreshold, ConfigError>.Ok(new OpeningsThreshold(value, unit, mode));
    }

    private static Result<OpeningsThreshold, ConfigError> Invalid(
        string message,
        string? category,
        string invalidValue) =>
        Result<OpeningsThreshold, ConfigError>.Err(
            new ConfigError(message) { Category = category, InvalidValue = invalidValue });

    public override string ToString() =>
        $"{Value.ToString(CultureInfo.InvariantCulture)} {Unit.Symbol()} ({Mode})";
}
