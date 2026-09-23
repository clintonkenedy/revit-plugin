namespace Metrado.Domain;

/// <summary>
/// One category's entry in a configuration file: the fields the user chose to
/// state, and nothing about the ones they left out.
/// </summary>
/// <remarks>
/// Every field but the category name is nullable, and <c>null</c> means exactly
/// one thing — inherit the built-in default for that field. It never means "empty"
/// or "zero".
/// <para>
/// The distinction matters most for <see cref="Sources"/>, where <c>null</c>
/// inherits and <c>[]</c> declares a category with no quantity source at all. That
/// empty list is how I2's counted categories are written, so a merge that treated
/// emptiness as absence would make them impossible to express.
/// </para>
/// </remarks>
public sealed record CategoryOverride
{
    public CategoryOverride(
        string category,
        QuantityUnit? unit = null,
        IReadOnlyList<string>? sources = null,
        double? threshold = null,
        BoundaryMode? mode = null,
        LayerOverride? layers = null)
    {
        Category = Guard.RequiredText(category, nameof(category));
        Unit = unit;
        Sources = sources;
        Threshold = threshold;
        Mode = mode;
        Layers = layers;
    }

    /// <summary>The category this entry configures. Always stated.</summary>
    public string Category { get; }

    /// <summary>The measurement unit, or null to inherit.</summary>
    public QuantityUnit? Unit { get; }

    /// <summary>The ordered quantity sources, or null to inherit. Empty is a choice.</summary>
    public IReadOnlyList<string>? Sources { get; }

    /// <summary>The openings threshold, or null to inherit.</summary>
    public double? Threshold { get; }

    /// <summary>The boundary convention at equality, or null to inherit.</summary>
    public BoundaryMode? Mode { get; }

    /// <summary>Material-layer takeoff turned off or on, or null to inherit.</summary>
    public LayerOverride? Layers { get; }
}
