namespace Metrado.Domain;

/// <summary>
/// What reading a criterion's quantity sources produced for one element: either a
/// quantity that was actually read, or nothing at all.
/// </summary>
/// <remarks>
/// A closed two-state type rather than a nullable <see cref="Quantity"/>. A wall
/// nobody measured and a wall measured at 0.0 m² are different facts, and a budget
/// that conflates them is wrong in a way no reviewer can see downstream — so the
/// distinction is carried by the type, not by a convention callers are asked to
/// remember.
/// <para>
/// <see cref="Match{T}"/> is the only way to the quantity, and it hands it to a
/// function that is never invoked when no source had a value. There is therefore
/// no expression of type <see cref="Quantity"/> derivable from an absent
/// selection: the conflation is not discouraged, it is unwritable. A nullable
/// <c>Quantity?</c> would not achieve this, because <c>GetValueOrDefault()</c>
/// manufactures a 0.0 that was never measured.
/// </para>
/// </remarks>
public sealed class SourceSelection
{
    private static readonly SourceSelection Absent = new(default, hasQuantity: false);

    private readonly Quantity _quantity;

    private SourceSelection(Quantity quantity, bool hasQuantity)
    {
        _quantity = quantity;
        HasQuantity = hasQuantity;
    }

    /// <summary>No source listed by the criterion had a value for this element.</summary>
    public static SourceSelection None => Absent;

    /// <summary>A source had a value, and this is the quantity it carried.</summary>
    public static SourceSelection Of(Quantity quantity) => new(quantity, hasQuantity: true);

    /// <summary>
    /// Whether a source was selected.
    /// </summary>
    /// <remarks>
    /// Deliberately not paired with a getter for the quantity. Knowing which state
    /// a selection is in is legitimate; obtaining a quantity from the state that
    /// has none is not, and <see cref="Match{T}"/> remains the only door.
    /// </remarks>
    public bool HasQuantity { get; }

    /// <summary>
    /// Collapses both states into one value, forcing the caller to say what an
    /// unmeasured element means before it can look at a measured one.
    /// </summary>
    /// <param name="selected">
    /// Given the quantity that was read. Never invoked when no source had a value.
    /// </param>
    /// <param name="none">Invoked when no source had a value.</param>
    /// <exception cref="ArgumentException">Either function is null.</exception>
    public T Match<T>(Func<Quantity, T> selected, Func<T> none)
    {
        Guard.RequiredValue(selected, nameof(selected));
        Guard.RequiredValue(none, nameof(none));

        return HasQuantity ? selected(_quantity) : none();
    }

    public override string ToString() =>
        HasQuantity ? $"Selected {_quantity}" : "No source";
}
