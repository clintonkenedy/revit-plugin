namespace Metrado.Domain;

/// <summary>
/// What a layer of a compound wall, floor or roof does, by Revit's own names
/// for it (<c>MaterialFunctionAssignment</c>), which are not localized. The
/// criteria file keys a layer's unit by these names (task 3.2).
/// </summary>
/// <remarks>Zero is left undeclared, so a default value is never taken for a function.</remarks>
public enum LayerFunction
{
    Structure = 1,
    Substrate = 2,
    Insulation = 3,
    Finish1 = 4,
    Finish2 = 5,
    Membrane = 6,
    StructuralDeck = 7,
}
