namespace Metrado.Domain;

/// <summary>
/// How a category taken off by material layer measures each layer function
/// (task 3.2): in m2 by default, in m3 where the criteria ask. A membrane
/// has no thickness, so it is always m2.
/// </summary>
/// <remarks>Seven get-only units rather than a dictionary, so two criteria stating the same units are equal.</remarks>
public sealed record LayerCriterion
{
    private LayerCriterion(QuantityUnit structure, QuantityUnit substrate, QuantityUnit insulation, QuantityUnit finish1, QuantityUnit finish2, QuantityUnit structuralDeck)
    {
        Structure = structure;
        Substrate = substrate;
        Insulation = insulation;
        Finish1 = finish1;
        Finish2 = finish2;
        StructuralDeck = structuralDeck;
    }

    /// <summary>Every function in m2 (the user's choice, PR 27).</summary>
    public static LayerCriterion Default { get; } = new(
        QuantityUnit.SquareMetre, QuantityUnit.SquareMetre, QuantityUnit.SquareMetre,
        QuantityUnit.SquareMetre, QuantityUnit.SquareMetre, QuantityUnit.SquareMetre);

    public QuantityUnit Structure { get; }

    public QuantityUnit Substrate { get; }

    public QuantityUnit Insulation { get; }

    public QuantityUnit Finish1 { get; }

    public QuantityUnit Finish2 { get; }

    public QuantityUnit StructuralDeck { get; }

    /// <summary>A membrane's unit, which is always m2.</summary>
    public QuantityUnit Membrane => QuantityUnit.SquareMetre;

    /// <summary>The functions the criteria state a unit for, over the default; a function left out keeps m2.</summary>
    public static Result<LayerCriterion, ConfigError> TryCreate(IReadOnlyDictionary<LayerFunction, QuantityUnit> units, string category)
    {
        Guard.RequiredValue(units, nameof(units));
        Guard.RequiredText(category, nameof(category));

        foreach (KeyValuePair<LayerFunction, QuantityUnit> entry in units)
        {
            string? refusal = !Enum.IsDefined(typeof(LayerFunction), entry.Key)
                ? $"{(int)entry.Key} is not a layer function."
                : entry.Value is not (QuantityUnit.SquareMetre or QuantityUnit.CubicMetre)
                    ? $"A {entry.Key} layer is measured in m2 or m3, not {Describe(entry.Value)}."
                : entry.Key == LayerFunction.Membrane && entry.Value == QuantityUnit.CubicMetre
                    ? "A Membrane layer has no thickness, so its volume is always 0: it is measured in m2."
                : null;
            if (refusal is not null)
            {
                return Result<LayerCriterion, ConfigError>.Err(new ConfigError($"In the layers of '{category}': {refusal}") { Category = category });
            }
        }

        QuantityUnit Of(LayerFunction function) => units.TryGetValue(function, out QuantityUnit unit) ? unit : QuantityUnit.SquareMetre;
        return Result<LayerCriterion, ConfigError>.Ok(new LayerCriterion(
            Of(LayerFunction.Structure), Of(LayerFunction.Substrate), Of(LayerFunction.Insulation),
            Of(LayerFunction.Finish1), Of(LayerFunction.Finish2), Of(LayerFunction.StructuralDeck)));
    }

    /// <exception cref="ArgumentOutOfRangeException">The function is not declared.</exception>
    public QuantityUnit UnitOf(LayerFunction function) => function switch
    {
        LayerFunction.Structure => Structure,
        LayerFunction.Substrate => Substrate,
        LayerFunction.Insulation => Insulation,
        LayerFunction.Finish1 => Finish1,
        LayerFunction.Finish2 => Finish2,
        LayerFunction.Membrane => Membrane,
        LayerFunction.StructuralDeck => StructuralDeck,
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Not a declared layer function."),
    };

    private static string Describe(QuantityUnit unit) =>
        Enum.IsDefined(typeof(QuantityUnit), unit) ? unit.Symbol() : "an undeclared unit";
}
