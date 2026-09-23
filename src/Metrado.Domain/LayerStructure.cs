namespace Metrado.Domain;

/// <summary>
/// A layered element's type, as it crosses the seam: its layers, exterior (or
/// top) first, and the facts under which an opening's share of a layer cannot
/// be told from its width.
/// </summary>
public sealed record LayerStructure
{
    /// <exception cref="ArgumentException">
    /// No layers, a null layer, positions not strictly ascending, or a
    /// condition undeclared or stated twice.
    /// </exception>
    public LayerStructure(IReadOnlyList<CompoundLayer> layers, IReadOnlyList<AddBackCondition> conditions)
    {
        Guard.RequiredValue(layers, nameof(layers));
        Guard.RequiredValue(conditions, nameof(conditions));

        if (layers.Count == 0)
        {
            throw new ArgumentException("A layered element has at least one layer.", nameof(layers));
        }

        for (int i = 0; i < layers.Count; i++)
        {
            Guard.RequiredValue(layers[i], nameof(layers));
            if (i > 0 && layers[i].Position <= layers[i - 1].Position)
            {
                throw new ArgumentException("Layers must come in their type's order, each position once.", nameof(layers));
            }
        }

        if (conditions.Any(condition => !Enum.IsDefined(typeof(AddBackCondition), condition))
            || conditions.Distinct().Count() != conditions.Count)
        {
            throw new ArgumentException("Each condition must be declared and stated once.", nameof(conditions));
        }

        Layers = layers;
        Conditions = conditions;
        Thickness = new Quantity(layers.Sum(layer => layer.Width.Value), QuantityUnit.Metre);
    }

    public IReadOnlyList<CompoundLayer> Layers { get; }

    public IReadOnlyList<AddBackCondition> Conditions { get; }

    /// <summary>The layers' widths added up, in metres.</summary>
    public Quantity Thickness { get; }
}
