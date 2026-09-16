namespace Metrado.Domain;

/// <summary>
/// The closed set of units a metrado may be expressed in.
/// </summary>
/// <remarks>
/// Members carry explicit values so that reordering the declaration cannot change
/// any member's identity, and so that zero stays undeclared: an uninitialized
/// <see cref="Quantity"/> must not silently claim to be square metres.
/// </remarks>
public enum QuantityUnit
{
    /// <summary>Area, written <c>m2</c>. The I1 unit for walls.</summary>
    SquareMetre = 1,

    /// <summary>Volume, written <c>m3</c>.</summary>
    CubicMetre = 2,

    /// <summary>Count of instances, written <c>u</c> (unidad).</summary>
    Each = 3,
}

/// <summary>
/// Canonical symbols for <see cref="QuantityUnit"/>. They live in Domain because
/// Domain owns the closed set: the criteria file reads these symbols and the
/// workbook writes them, so both sides must agree on one spelling.
/// </summary>
public static class QuantityUnitExtensions
{
    /// <summary>The symbol the specifications use for this unit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The unit is not a declared member.</exception>
    public static string Symbol(this QuantityUnit unit) => unit switch
    {
        QuantityUnit.SquareMetre => "m2",
        QuantityUnit.CubicMetre => "m3",
        QuantityUnit.Each => "u",
        _ => throw new ArgumentOutOfRangeException(
            nameof(unit), unit, "Not a declared quantity unit."),
    };
}
