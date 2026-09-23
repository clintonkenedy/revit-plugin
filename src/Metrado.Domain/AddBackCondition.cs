namespace Metrado.Domain;

/// <summary>
/// A fact about a layered type under which an opening's share of a layer is
/// not its area times the layer's width, so an opening added back to the
/// host cannot be handed out to the layers (task 3.2).
/// </summary>
/// <remarks>Zero is left undeclared, so a default value is never taken for a condition.</remarks>
public enum AddBackCondition
{
    /// <summary>The type's layers wrap at inserts.</summary>
    WrapsAtInserts = 1,

    /// <summary>A vertically compound wall type: its layers change up its height.</summary>
    VerticallyCompound = 2,

    /// <summary>A layer of variable thickness, such as a tapered roof insulation.</summary>
    VariableLayer = 3,

    /// <summary>A floor or roof whose shape was edited point by point.</summary>
    ShapeEdited = 4,

    /// <summary>A structural deck layer, whose profile is not its width times its area.</summary>
    StructuralDeck = 5,
}
