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
    /// <param name="layers">
    /// How each material layer is measured, when the category is taken off by
    /// layer (task 3.2); null to measure each element whole.
    /// </param>
    public CategoryCriterion(
        string category,
        QuantityUnit unit,
        IReadOnlyList<string> sources,
        OpeningsThreshold threshold,
        LayerCriterion? layers = null)
    {
        Category = Guard.RequiredText(category, nameof(category));
        Sources = Guard.RequiredValue(sources, nameof(sources));

        // No source means counted (N1), and a count is in units: square metres
        // of doors is not a quantity anyone can price.
        if (sources.Count == 0 && unit != QuantityUnit.Each)
        {
            throw new ArgumentException(
                $"The {category} criterion lists no quantity source, so it counts instances, and a count is in {QuantityUnit.Each.Symbol()}, not {unit}.",
                nameof(unit));
        }

        if (layers is not null && !LayeredCategories.Contains(category))
        {
            throw new ArgumentException($"The {category} criterion cannot be taken off by material layer: only walls, floors and roofs have layers.", nameof(layers));
        }

        // A layered element falls back to whole when its layers cannot be
        // trusted, and its openings and threshold are areas: only an area
        // measured from a source can be taken off by layer. Layer lines are
        // corrected without the whole path's unit check, so the threshold's
        // unit is checked here, which also refuses a default threshold.
        if (layers is not null && (unit != QuantityUnit.SquareMetre || sources.Count == 0 || threshold.Unit != QuantityUnit.SquareMetre))
        {
            throw new ArgumentException(
                $"The {category} criterion is taken off by material layer, so it is measured in m2 from a quantity source, with its threshold in m2.",
                nameof(layers));
        }

        Unit = unit;
        Threshold = threshold;
        Layers = layers;
    }

    /// <summary>The categories that can be taken off by material layer: the hosts, whose types have layers.</summary>
    public static IReadOnlyCollection<string> LayeredCategories { get; } = new HashSet<string>(StringComparer.Ordinal) { "Walls", "Floors", "Roofs" };

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

    /// <summary>How each material layer is measured; null when each element is measured whole.</summary>
    public LayerCriterion? Layers { get; }
}
