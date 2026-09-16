namespace Metrado.Domain;

/// <summary>
/// How one category is measured: the unit its metrado is expressed in, the
/// quantity sources to read in order, and the openings threshold to apply.
/// </summary>
/// <remarks>
/// <c>Sources</c> is ordered and the order is meaningful — the first source with a
/// value is the one measured, so reordering it changes the budget.
/// <para>
/// The source keys stay plain strings so the user-nominated sources arriving in I2
/// need no Domain change, matching <see cref="RawQuantity.SourceKey"/> on the
/// other side of the Revit seam.
/// </para>
/// </remarks>
public sealed record CategoryCriterion
{
    public CategoryCriterion(
        string category,
        QuantityUnit unit,
        IReadOnlyList<string> sources,
        OpeningsThreshold threshold)
    {
        Category = Guard.RequiredText(category, nameof(category));
        Sources = Guard.RequiredValue(sources, nameof(sources));
        Unit = unit;
        Threshold = threshold;
    }

    /// <summary>The Revit category this criterion measures.</summary>
    public string Category { get; }

    /// <summary>The unit this category's metrado is expressed in.</summary>
    public QuantityUnit Unit { get; }

    /// <summary>
    /// The quantity sources to read, in priority order. Empty means no source is
    /// listed at all; I2's count-based measurement branches on that before any
    /// source is read, so that a counted category is never reported as a category
    /// whose sources all came up empty.
    /// </summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The openings threshold and boundary convention for this category.</summary>
    public OpeningsThreshold Threshold { get; }
}
