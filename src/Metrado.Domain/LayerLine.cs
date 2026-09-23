namespace Metrado.Domain;

/// <summary>
/// One material of a layered element and the layers it covers, in the
/// type's order: Revit measures a material used by two layers as one.
/// </summary>
public sealed record MaterialLayers
{
    /// <exception cref="ArgumentException">No layers, or a layer of another material.</exception>
    public MaterialLayers(MaterialRef material, IReadOnlyList<CompoundLayer> layers)
    {
        Material = Guard.RequiredValue(material, nameof(material));
        Layers = Guard.RequiredValue(layers, nameof(layers));
        if (layers.Count == 0 || layers.Any(layer => layer.MaterialId != material.MaterialId))
        {
            throw new ArgumentException($"A material's layers are at least one, each of {material.MaterialId}.", nameof(layers));
        }
    }

    public MaterialRef Material { get; }

    public IReadOnlyList<CompoundLayer> Layers { get; }

    /// <summary>Where the material first appears, exterior (or top) first: the lines' order.</summary>
    public int FirstPosition => Layers.Min(layer => layer.Position);

    /// <summary>Its layers' widths added up, in metres.</summary>
    public Quantity Width => new(Layers.Sum(layer => layer.Width.Value), QuantityUnit.Metre);

    public int LayerCount => Layers.Count;
}

/// <summary>One material of a layered element, measured.</summary>
public sealed record LayerLine(MaterialLayers Layer, MetradoResult Metrado);

/// <summary>
/// A layered element measured by its materials, with any warnings; or
/// measured whole, with the reason. Two states and nothing between, as a
/// partly measured element must never reach the budget.
/// </summary>
public sealed class LayerOutcome
{
    private readonly IReadOnlyList<LayerLine>? _lines;
    private readonly IReadOnlyList<ValidationWarning>? _warnings;
    private readonly ValidationWarning? _why;

    private LayerOutcome(IReadOnlyList<LayerLine>? lines, IReadOnlyList<ValidationWarning>? warnings, ValidationWarning? why)
    {
        _lines = lines;
        _warnings = warnings;
        _why = why;
    }

    public static LayerOutcome ByLayer(IReadOnlyList<LayerLine> lines, IReadOnlyList<ValidationWarning> warnings) =>
        new(Guard.RequiredValue(lines, nameof(lines)), Guard.RequiredValue(warnings, nameof(warnings)), null);

    public static LayerOutcome Whole(ValidationWarning why) => new(null, null, Guard.RequiredValue(why, nameof(why)));

    public T Match<T>(Func<IReadOnlyList<LayerLine>, IReadOnlyList<ValidationWarning>, T> byLayer, Func<ValidationWarning, T> whole)
    {
        Guard.RequiredValue(byLayer, nameof(byLayer));
        Guard.RequiredValue(whole, nameof(whole));
        return _why is null ? byLayer(_lines!, _warnings!) : whole(_why);
    }
}
