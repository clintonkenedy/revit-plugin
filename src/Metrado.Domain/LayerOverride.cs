namespace Metrado.Domain;

/// <summary>
/// What a criteria file says of a category's material layers: off, or on with
/// the units it states per function. An entry that says nothing is null on
/// <see cref="CategoryOverride.Layers"/> and inherits.
/// </summary>
public sealed class LayerOverride
{
    private readonly IReadOnlyDictionary<LayerFunction, QuantityUnit>? _units;

    private LayerOverride(IReadOnlyDictionary<LayerFunction, QuantityUnit>? units) => _units = units;

    /// <summary>Taken off whole, stated.</summary>
    public static LayerOverride Off { get; } = new(null);

    /// <summary>Taken off by material layer, in the units stated per function (m2 for any left out).</summary>
    public static LayerOverride On(IReadOnlyDictionary<LayerFunction, QuantityUnit> units) =>
        new(Guard.RequiredValue(units, nameof(units)));

    public T Match<T>(Func<T> off, Func<IReadOnlyDictionary<LayerFunction, QuantityUnit>, T> on)
    {
        Guard.RequiredValue(off, nameof(off));
        Guard.RequiredValue(on, nameof(on));
        return _units is null ? off() : on(_units);
    }
}
