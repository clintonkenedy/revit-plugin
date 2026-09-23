using System.Globalization;

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

    /// <summary>
    /// Measures a layered element by its materials, one line each, exterior
    /// (or top) first, in the unit its layers' function has in the criterion;
    /// or, when its materials do not account for all of it, whole.
    /// </summary>
    /// <remarks>
    /// Each opening is decided once, at the host's threshold. One added back
    /// returns to an m2 line its area times the material's layer count: PR 28
    /// found each material's area gets back the host's gain times its layer
    /// count when an opening is deleted. An m3 line keeps Revit's deduction,
    /// the volume share not being exact, and so does every line of a type
    /// under a condition; one warning says which.
    /// </remarks>
    /// <exception cref="ArgumentException">The criterion does not take the category off by layer.</exception>
    public static LayerOutcome Measure(ElementTakeoff element, CategoryCriterion criterion)
    {
        Guard.RequiredValue(element, nameof(element));
        Guard.RequiredValue(criterion, nameof(criterion));
        LayerCriterion units = criterion.Layers
            ?? throw new ArgumentException($"The {criterion.Category} criterion does not take it off by layer.", nameof(criterion));

        LayerReconciliation reconciliation = Reconcile(element);
        if (!reconciliation.Reconciles)
        {
            return LayerOutcome.Whole(reconciliation.WarningFor(element)!);
        }

        if (element.Openings.FirstOrDefault(opening => opening.Amount.Unit != criterion.Threshold.Unit) is OpeningQuantity foreign)
        {
            return LayerOutcome.Whole((reconciliation with { Fault = LayerFault.UnitMismatch, Detail = $"opening {foreign.UniqueId} in {foreign.Amount.Unit.Symbol()}" }).WarningFor(element)!);
        }

        Dictionary<string, RawQuantity> volumes = element.Quantities
            .Where(quantity => quantity.Material is not null && quantity.SourceKey == LayerSources.MaterialVolume)
            .ToDictionary(quantity => quantity.Material!.MaterialId, StringComparer.Ordinal);
        Dictionary<string, RawQuantity> areas = element.Quantities
            .Where(quantity => quantity.Material is not null && quantity.SourceKey == LayerSources.MaterialArea)
            .ToDictionary(quantity => quantity.Material!.MaterialId, StringComparer.Ordinal);
        IReadOnlyList<AddBackCondition> conditions = element.Layers!.Conditions;
        List<Quantity> openings = [.. element.Openings.Select(opening => opening.Amount)];

        List<LayerLine> lines = [];
        List<ValidationWarning> warnings = [];
        List<MaterialLayers> kept = [];
        foreach (IGrouping<string, CompoundLayer> material in element.Layers.Layers
            .Where(layer => layer.MaterialId is not null && volumes.ContainsKey(layer.MaterialId))
            // Grouped over the layers, which the structure keeps in order, so
            // the lines come exterior (or top) first whatever order Revit lists
            // its materials in.
            .GroupBy(layer => layer.MaterialId!, StringComparer.Ordinal))
        {
            List<QuantityUnit> materialUnits = [.. material.Select(layer => units.UnitOf(layer.Function!.Value)).Distinct()];
            MaterialRef reference = volumes[material.Key].Material!;
            if (materialUnits.Count > 1)
            {
                return LayerOutcome.Whole((reconciliation with { Fault = LayerFault.UnitConflict, Detail = reference.MaterialName }).WarningFor(element)!);
            }

            bool squareMetres = materialUnits[0] == QuantityUnit.SquareMetre;
            bool shared = squareMetres && conditions.Count == 0;
            MetradoOutcome outcome = Measurement.Correct(
                element,
                (squareMetres ? areas[material.Key] : volumes[material.Key]).Amount,
                openings,
                criterion.Threshold,
                shared ? material.Count() : 0);
            MaterialLayers layer = new(reference, [.. material]);
            lines.Add(new LayerLine(layer, outcome.Result!));
            if (outcome.Warning is not null)
            {
                warnings.Add(outcome.Warning);
            }

            if (!shared)
            {
                kept.Add(layer);
            }
        }

        List<OpeningQuantity> addedBack = [.. element.Openings.Where(opening => Measurement.IsAddedBack(opening.Amount, criterion.Threshold))];
        if (kept.Count > 0 && addedBack.Count > 0)
        {
            warnings.Add(Kept(element, kept, addedBack, conditions));
        }

        return LayerOutcome.ByLayer(lines, warnings);
    }

    /// <summary>
    /// The lines that keep Revit's deduction of openings the threshold adds
    /// back. Under a condition no amount is stated, since the condition is
    /// that no share can be told; an m3 line with no condition is told about
    /// how much, its layers' width times the openings' area.
    /// </summary>
    private static ValidationWarning Kept(ElementTakeoff element, List<MaterialLayers> kept, List<OpeningQuantity> addedBack, IReadOnlyList<AddBackCondition> conditions)
    {
        double area = addedBack.Sum(opening => opening.Amount.Value);
        string openings = string.Format(
            CultureInfo.InvariantCulture,
            "The openings the threshold adds back to this element ({0}, {1:0.###} m2 in all) are not returned to its layer lines",
            string.Join(", ", addedBack.Select(opening => opening.UniqueId)),
            area);

        // With no condition only a line in m3 is kept.
        return ValidationWarning.ForElement(
            element,
            conditions.Count > 0
                ? $"{openings} {string.Join(", ", kept.Select(layer => layer.Material.MaterialName))}: {string.Join("; ", conditions.Select(Why))}, "
                    + "so how much each layer lost to them cannot be told from its width. Each of those lines keeps Revit's deduction of them, whatever it was."
                : $"{openings} in m3, since a share of volume is not exact: each keeps Revit's deduction of them, about their area times its layers' width ("
                    + string.Join(", ", kept.Select(layer => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.###} m3", layer.Material.MaterialName, area * layer.Width.Value)))
                    + ").");
    }

    private static string Why(AddBackCondition condition) => condition switch
    {
        AddBackCondition.WrapsAtInserts => "its layers wrap at inserts, or a door or window in it sets its own Wall Closure",
        AddBackCondition.VerticallyCompound => "its type is vertically compound",
        AddBackCondition.VariableLayer => "a layer of its type varies in thickness",
        AddBackCondition.ShapeEdited => "its shape is edited",
        AddBackCondition.StructuralDeck => "its type has a structural deck",
        _ => condition.ToString(),
    };

    private static LayerReconciliation Fault(Quantity? whole, double sum, int terms, LayerFault fault, string? detail = null) =>
        new(whole, sum, terms, fault, detail);

    private static bool Readable(double value) => value >= 0 && !double.IsInfinity(value);

    private static string? Repeated(IEnumerable<RawQuantity> quantities) =>
        quantities.GroupBy(quantity => quantity.Material!.MaterialId).FirstOrDefault(group => group.Count() > 1)?.Key;
}
