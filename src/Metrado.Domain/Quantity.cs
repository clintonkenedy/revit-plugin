namespace Metrado.Domain;

/// <summary>
/// A measured amount together with the unit it is expressed in. Always already
/// converted: Revit's imperial internal units are converted in the Revit-facing
/// layer, and Domain never converts (see <c>metrado-measurement</c>).
/// </summary>
/// <remarks>
/// The unit is part of the value's identity, so 18 m² and 18 m³ are different
/// quantities and no downstream code can treat them as interchangeable.
/// </remarks>
public readonly record struct Quantity(double Value, QuantityUnit Unit)
{
    /// <summary>
    /// Renders an undeclared unit rather than throwing. A quantity whose unit was
    /// never set is exactly the value most likely to appear in a failure message,
    /// and diagnostics must not raise a second exception on top of the first.
    /// </summary>
    public override string ToString() => Enum.IsDefined(typeof(QuantityUnit), Unit)
        ? $"{Value} {Unit.Symbol()}"
        : $"{Value} (undeclared unit)";
}
