namespace Metrado.Domain;

/// <summary>
/// How an opening whose quantity is exactly equal to the threshold is treated.
/// A closed domain of exactly two values — there is no third mode and no
/// free-form comparison expression.
/// </summary>
/// <remarks>
/// Both readings are defensible: metrado norms are written either as "no se
/// descuentan los vanos de área <em>menor a</em> X" (exclusive) or "vanos
/// <em>hasta</em> X" (inclusive), and the reference product does not settle the
/// equality case. The mode affects only openings exactly equal to the threshold;
/// every other opening behaves identically under both.
/// <para>
/// Members carry explicit values and zero is left undeclared, so reordering the
/// declaration cannot change what a member means and a mode that was never set
/// is detectable rather than silently valid.
/// </para>
/// </remarks>
public enum BoundaryMode
{
    /// <summary><c>q(o) &lt; threshold</c>. The product default.</summary>
    Exclusive = 1,

    /// <summary><c>q(o) &lt;= threshold</c>.</summary>
    Inclusive = 2,
}

/// <summary>
/// Product-wide defaults that are conventions rather than configuration.
/// </summary>
public static class Defaults
{
    /// <summary>
    /// The default boundary mode, pinned by naming the member.
    /// </summary>
    /// <remarks>
    /// Never written as an ordinal. Deriving this from a position would let an
    /// enum reorder silently flip the metrado convention, and a convention that
    /// changes without appearing in the output produces two different defensible
    /// budgets from one model with no way to tell them apart.
    /// </remarks>
    public const BoundaryMode Mode = BoundaryMode.Exclusive;
}
