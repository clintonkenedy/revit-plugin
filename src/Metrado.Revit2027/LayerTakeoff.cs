using System.Globalization;
using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>Revit's own conversions from its internal units, injected so the mappings run outside Revit.</summary>
public sealed record SeamUnits(Func<double, double> SquareMetres, Func<double, double> CubicMetres, Func<double, double> Metres);

/// <param name="Function">The layer's <c>MaterialFunctionAssignment</c>, by its enum name.</param>
/// <param name="MaterialFromCategory">The layer states no material, and Revit gives it the category's.</param>
/// <param name="CapFlag">The layer's <c>LayerCapFlag</c>, recorded for the probe.</param>
public sealed record LayerFacts(string Function, double WidthFeet, string? MaterialUniqueId, bool MaterialFromCategory, bool CapFlag);

/// <summary>The type facts under which an opening's share of a layer is not its area times the layer's width.</summary>
public sealed record TypeFacts(bool WrapsAtInserts, bool VerticallyCompound, bool VariableLayer, bool ShapeEdited);

/// <summary>One material Revit measures in the element, its codes read on the material itself.</summary>
public sealed record MaterialReading(
    string UniqueId, string Name, double VolumeCubicFeet, double AreaSquareFeet, string? Keynote, IReadOnlyDictionary<string, string?> SharedParameters);

/// <summary>A paint applied to the element's faces, with the area it covers.</summary>
public sealed record PaintReading(string Name, double AreaSquareFeet);

/// <summary>
/// A layered host's layers as read, in Revit's internal units: its computed
/// volume, its type's layers exterior (or top) first, the materials Revit
/// measures in it, its paints, and its type's facts.
/// </summary>
public sealed record LayerReading(
    double? ComputedVolumeCubicFeet,
    IReadOnlyList<LayerFacts> Layers,
    IReadOnlyList<MaterialReading> Materials,
    IReadOnlyList<PaintReading> Paint,
    TypeFacts Type);

/// <summary>
/// Turns a <see cref="LayerReading"/> into what crosses the seam (task 3.1):
/// each material Revit measures as one volume and one area entry, carrying
/// the material and its own codes, beside the whole element's volume; and
/// the type's layers as a <see cref="LayerStructure"/>. Holds no Revit type.
/// </summary>
public static class LayerTakeoff
{
    public static (IReadOnlyList<RawQuantity> Quantities, LayerStructure? Structure) From(LayerReading reading, SeamUnits units)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(units);

        // Rounded to the nano-unit, as areas are; a value Revit could not give
        // is left out, never read as zero.
        List<RawQuantity> quantities = [];
        void Add(string source, double value, Func<double, double> convert, QuantityUnit unit, MaterialRef? material)
        {
            if (double.IsFinite(value))
            {
                quantities.Add(new RawQuantity(source, new Quantity(Math.Round(convert(value), 9), unit), material));
            }
        }

        if (reading.ComputedVolumeCubicFeet is double volume)
        {
            Add(LayerSources.HostVolume, volume, units.CubicMetres, QuantityUnit.CubicMetre, null);
        }

        foreach (MaterialReading material in reading.Materials)
        {
            MaterialRef reference = new(material.UniqueId, material.Name, new CodificationReadings(null, material.Keynote, material.SharedParameters));
            Add(LayerSources.MaterialVolume, material.VolumeCubicFeet, units.CubicMetres, QuantityUnit.CubicMetre, reference);
            Add(LayerSources.MaterialArea, material.AreaSquareFeet, units.SquareMetres, QuantityUnit.SquareMetre, reference);
        }

        return (quantities, Structure(reading, units));
    }

    /// <summary>
    /// One warning for a painted host, naming each paint and its area. Paint
    /// is not taken off, so without it the paint would be in no line of the
    /// budget without a word.
    /// </summary>
    public static IReadOnlyList<ValidationWarning> Paint(ElementTakeoff takeoff, LayerReading reading, Func<double, double> squareMetres)
    {
        ArgumentNullException.ThrowIfNull(takeoff);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(squareMetres);

        if (reading.Paint.Count == 0)
        {
            return [];
        }

        IEnumerable<string> paints = reading.Paint.Select(paint =>
            $"{paint.Name} over {Math.Round(squareMetres(paint.AreaSquareFeet), 3).ToString("0.###", CultureInfo.InvariantCulture)} m2");
        return [ValidationWarning.ForElement(
            takeoff,
            $"This {HostTakeoff.Noun(takeoff.CategoryName)} is painted: {string.Join(", ", paints)}. "
            + "Paint is not taken off, so it is in no line of the budget: price it by hand.")];
    }

    /// <summary>The type's layers, or none when there are none or one's width could not be read: never an invented structure.</summary>
    private static LayerStructure? Structure(LayerReading reading, SeamUnits units)
    {
        if (reading.Layers.Count == 0 || reading.Layers.Any(layer => !double.IsFinite(layer.WidthFeet) || layer.WidthFeet < 0))
        {
            return null;
        }

        List<CompoundLayer> layers = [.. reading.Layers.Select((layer, index) =>
        {
            string? material = string.IsNullOrWhiteSpace(layer.MaterialUniqueId) ? null : layer.MaterialUniqueId;
            return new CompoundLayer(
                index,
                Function(layer.Function),
                new Quantity(Math.Round(units.Metres(layer.WidthFeet), 9), QuantityUnit.Metre),
                material,
                layer.MaterialFromCategory && material is not null);
        })];

        List<AddBackCondition> conditions = [];
        if (reading.Type.WrapsAtInserts)
        {
            conditions.Add(AddBackCondition.WrapsAtInserts);
        }

        if (reading.Type.VerticallyCompound)
        {
            conditions.Add(AddBackCondition.VerticallyCompound);
        }

        if (reading.Type.VariableLayer)
        {
            conditions.Add(AddBackCondition.VariableLayer);
        }

        if (reading.Type.ShapeEdited)
        {
            conditions.Add(AddBackCondition.ShapeEdited);
        }

        if (layers.Any(layer => layer.Function == LayerFunction.StructuralDeck))
        {
            conditions.Add(AddBackCondition.StructuralDeck);
        }

        return new LayerStructure(layers, conditions);
    }

    /// <summary>A function by Revit's exact name; "None", a number or anything unknown is no function.</summary>
    private static LayerFunction? Function(string name) =>
        Enum.TryParse(name, out LayerFunction function) && Enum.GetName(function) == name ? function : null;
}
