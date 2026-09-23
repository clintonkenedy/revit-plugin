using System.Globalization;

namespace Metrado.Domain;

/// <summary>Why a layered element cannot be measured by its layers, and is measured whole instead.</summary>
public enum LayerFault
{
    NoLayers = 1,
    NoWholeVolume = 2,
    UnitMismatch = 3,
    DuplicateMaterial = 4,
    NonFiniteOrNegative = 5,
    UnattributedLayer = 6,
    UnattributedMaterial = 7,
    NoFunction = 8,
    UnitConflict = 9,
    OutOfTolerance = 10,
}

/// <summary>
/// Whether Revit's materials account for all of a layered element (task
/// 3.2): the whole volume, the materials' volumes added up, how many there
/// are, and the first fault found, if any.
/// </summary>
/// <param name="Detail">What the fault concerns, in words, for its warning.</param>
public sealed record LayerReconciliation(Quantity? Whole, double Sum, int Terms, LayerFault? Fault, string? Detail = null)
{
    public bool Reconciles => Fault is null;

    /// <summary>The warning an element measured whole for this fault carries; null when it reconciles.</summary>
    public ValidationWarning? WarningFor(ElementTakeoff element) => Fault switch
    {
        null => null,
        LayerFault fault => ValidationWarning.ForElement(
            Guard.RequiredValue(element, nameof(element)),
            $"Not taken off by material layer, so measured whole: {Explain(fault)}."),
    };

    private string Explain(LayerFault fault) => fault switch
    {
        LayerFault.NoLayers => "its type's layers could not be read",
        LayerFault.NoWholeVolume => "Revit gives no volume for the whole element to check its materials against",
        LayerFault.UnitMismatch => $"a volume or area crossed in the wrong unit ({Detail})",
        LayerFault.DuplicateMaterial => $"material {Detail} was read twice",
        LayerFault.NonFiniteOrNegative => $"a volume or area of {Detail} could not be read, or is negative",
        LayerFault.UnattributedLayer => $"layer {Detail} is not among the materials Revit measures in the element",
        LayerFault.UnattributedMaterial => $"material {Detail}, which Revit measures in the element, is on none of its layers",
        LayerFault.NoFunction => $"layer {Detail} has no function, so no unit can be chosen for it",
        LayerFault.UnitConflict => $"material {Detail} covers layers whose functions are measured in different units",
        LayerFault.OutOfTolerance => string.Format(
            CultureInfo.InvariantCulture,
            "its materials' volumes add up to {0:0.#########} m3 against the whole element's {1:0.#########} m3, more than {2} m3 apart",
            Sum,
            Whole?.Value ?? double.NaN,
            LayerMeasurement.ToleranceCubicMetres),
        _ => fault.ToString(),
    };
}
