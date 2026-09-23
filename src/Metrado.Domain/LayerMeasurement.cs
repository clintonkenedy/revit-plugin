namespace Metrado.Domain;

/// <summary>
/// Measures a layered element by the materials Revit measures in it (task
/// 3.2), or decides it must be measured whole.
/// </summary>
public static class LayerMeasurement
{
    /// <summary>
    /// A cubic centimetre, fixed: an error in a volume does not grow with the
    /// element. The seam rounds each term to 1e-9 m3; the smallest real
    /// material seen on the samples was 0.0031 m3.
    /// </summary>
    public const double ToleranceCubicMetres = 1e-6;

    /// <summary>
    /// Whether Revit's materials account for all of the element, checked in
    /// order: its layers were read; its whole volume was, in m3; each
    /// material's volume and area were read once, in their units, finite and
    /// not negative; every layer of positive width is a material Revit
    /// measures, and every such material is on a layer; every layer has a
    /// function; and the volumes add up to the whole within the tolerance.
    /// </summary>
    public static LayerReconciliation Reconcile(ElementTakeoff element)
    {
        Guard.RequiredValue(element, nameof(element));

        if (element.Layers is not LayerStructure structure)
        {
            return Fault(null, 0, 0, LayerFault.NoLayers);
        }

        if (element.Quantities.FirstOrDefault(quantity => quantity.Material is null && quantity.SourceKey == LayerSources.HostVolume) is not RawQuantity wholeEntry)
        {
            return Fault(null, 0, 0, LayerFault.NoWholeVolume);
        }

        Quantity whole = wholeEntry.Amount;
        List<RawQuantity> volumes = [.. element.Quantities.Where(quantity => quantity.Material is not null && quantity.SourceKey == LayerSources.MaterialVolume)];
        List<RawQuantity> areas = [.. element.Quantities.Where(quantity => quantity.Material is not null && quantity.SourceKey == LayerSources.MaterialArea)];
        double sum = volumes.Sum(volume => volume.Amount.Value);

        if (whole.Unit != QuantityUnit.CubicMetre
            || volumes.FirstOrDefault(volume => volume.Amount.Unit != QuantityUnit.CubicMetre) is not null
            || areas.FirstOrDefault(area => area.Amount.Unit != QuantityUnit.SquareMetre) is not null)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.UnitMismatch, "a volume must be in m3 and an area in m2");
        }

        if ((Repeated(volumes) ?? Repeated(areas)) is string repeated)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.DuplicateMaterial, repeated);
        }

        HashSet<string> measured = [.. volumes.Select(volume => volume.Material!.MaterialId)];
        HashSet<string> areaMeasured = [.. areas.Select(area => area.Material!.MaterialId)];
        if (!Readable(whole.Value))
        {
            return Fault(whole, sum, volumes.Count, LayerFault.NonFiniteOrNegative, "the whole element");
        }

        if (volumes.Concat(areas).FirstOrDefault(quantity => !Readable(quantity.Amount.Value)) is RawQuantity unreadable)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.NonFiniteOrNegative, unreadable.Material!.MaterialName);
        }

        if (measured.Concat(areaMeasured).FirstOrDefault(id => !measured.Contains(id) || !areaMeasured.Contains(id)) is string half)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.NonFiniteOrNegative, half);
        }

        if (structure.Layers.FirstOrDefault(layer => layer.Width.Value > 0 && (layer.MaterialId is null || !measured.Contains(layer.MaterialId))) is CompoundLayer lost)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.UnattributedLayer, $"{lost.Position} ({lost.Function?.ToString() ?? "no function"})");
        }

        if (measured.FirstOrDefault(id => !structure.Layers.Any(layer => layer.MaterialId == id)) is string stray)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.UnattributedMaterial, stray);
        }

        if (structure.Layers.FirstOrDefault(layer => layer.MaterialId is not null && measured.Contains(layer.MaterialId) && layer.Function is null) is CompoundLayer unnamed)
        {
            return Fault(whole, sum, volumes.Count, LayerFault.NoFunction, unnamed.Position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Written as a positive `<=` so a NaN on either side fails.
        return Math.Abs(sum - whole.Value) <= ToleranceCubicMetres
            ? new LayerReconciliation(whole, sum, volumes.Count, null)
            : Fault(whole, sum, volumes.Count, LayerFault.OutOfTolerance);
    }

    private static LayerReconciliation Fault(Quantity? whole, double sum, int terms, LayerFault fault, string? detail = null) =>
        new(whole, sum, terms, fault, detail);

    private static bool Readable(double value) => value >= 0 && !double.IsInfinity(value);

    private static string? Repeated(IEnumerable<RawQuantity> quantities) =>
        quantities.GroupBy(quantity => quantity.Material!.MaterialId).FirstOrDefault(group => group.Count() > 1)?.Key;
}
