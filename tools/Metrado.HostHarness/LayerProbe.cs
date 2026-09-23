using Autodesk.Revit.DB;
using Metrado.Domain;
using Metrado.Revit2027;

namespace Metrado.HostHarness;

/// <summary>
/// Reads every wall, floor and roof in force through Metrado's own
/// <see cref="MaterialLayerReader"/> and <see cref="LayerTakeoff"/> (task
/// 3.1), and checks what reaches the seam against Revit: the materials'
/// volumes against the element's, and each layer against the materials
/// Revit measures. As a positive control, it sets a Keynote on one material
/// in a transaction that is rolled back and reads it back through the
/// reader. The model ends exactly as it started and is never saved.
/// </summary>
public static class LayerProbe
{
    /// <param name="Fault">Null when the element's materials reconcile and every layer is attributed.</param>
    public sealed record HostResult(string Category, string UniqueId, string TypeName, int Layers, int Materials, double? VolumeGapM3, List<string> Conditions, List<string> Notes, string? Fault);

    public sealed record Result(
        int Hosts, int NoStructure, int Reconciled, int Faulted, double WorstGapM3, List<HostResult> Notable,
        Dictionary<string, int> ConditionCounts, string? KeynoteControl, bool ModifiedAfterProbe,
        Dictionary<string, int> WallClosures);

    private const double ToleranceCubicMetres = 1e-6;

    public static Result Run(Document document)
    {
        List<(HostObject Host, string Key)> hosts =
        [
            .. WallReader.Walls(document).Select(wall => ((HostObject)wall, HostTakeoff.WallsKey)),
            .. SurfaceReader.Surfaces(document, BuiltInCategory.OST_Floors).Select(floor => (floor, HostTakeoff.FloorsKey)),
            .. SurfaceReader.Surfaces(document, BuiltInCategory.OST_Roofs).Select(roof => (roof, HostTakeoff.RoofsKey)),
        ];

        List<HostResult> results = [];
        int noStructure = 0;
        foreach ((HostObject host, string key) in hosts)
        {
            if (MaterialLayerReader.Read(document, host, null) is not LayerReading reading)
            {
                noStructure++;
                continue;
            }

            results.Add(Check(document, host, key, reading));
        }

        return new Result(
            results.Count + noStructure,
            noStructure,
            results.Count(result => result.Fault is null),
            results.Count(result => result.Fault is not null),
            results.Where(result => result.VolumeGapM3 is not null).Select(result => result.VolumeGapM3!.Value).DefaultIfEmpty(0).Max(),
            [.. results.Where(result => result.Fault is not null || result.Notes.Count > 0 || result.Conditions.Count > 0).Take(60)],
            results.SelectMany(result => result.Conditions).GroupBy(condition => condition).ToDictionary(group => group.Key, group => group.Count()),
            KeynoteControl(document, hosts),
            document.IsModified,
            WallClosures(document, hosts));
    }

    private static HostResult Check(Document document, HostObject host, string key, LayerReading reading)
    {
        (IReadOnlyList<RawQuantity> quantities, LayerStructure? structure) = LayerTakeoff.From(reading, ExtractionService.Units);
        double? whole = quantities.FirstOrDefault(quantity => quantity.SourceKey == LayerSources.HostVolume)?.Amount.Value;
        List<RawQuantity> volumes = [.. quantities.Where(quantity => quantity.SourceKey == LayerSources.MaterialVolume)];
        double? gap = whole is double volume ? Math.Abs(volumes.Sum(quantity => quantity.Amount.Value) - volume) : null;

        HashSet<string> measured = [.. volumes.Select(quantity => quantity.Material!.MaterialId)];
        List<CompoundLayer> layers = [.. structure?.Layers ?? []];
        List<string> notes = [];
        notes.AddRange(layers.Where(layer => layer.MaterialFromCategory).Select(layer => $"layer {layer.Position} has the category's material"));
        notes.AddRange(layers.Where(layer => layer.MaterialId is null).Select(layer => $"layer {layer.Position} has no material at all"));
        notes.AddRange(reading.Layers.Where(layer => layer.CapFlag).Select(layer => $"cap flag on a {layer.Function} layer"));
        notes.AddRange(reading.Paint.Select(paint => $"painted: {paint.Name}"));

        string? fault = structure is null ? "no structure crossed"
            : whole is null ? "no whole volume"
            : gap > ToleranceCubicMetres ? $"materials miss the volume by {gap:E2} m3"
            : layers.FirstOrDefault(layer => layer.Width.Value > 0 && (layer.MaterialId is null || !measured.Contains(layer.MaterialId))) is CompoundLayer lost
                ? $"layer {lost.Position} ({lost.Function}) is not among the materials Revit measures"
            : measured.FirstOrDefault(id => !layers.Any(layer => layer.MaterialId == id)) is string stray
                ? $"material {stray} is on no layer"
            : null;

        return new HostResult(
            key, host.UniqueId, document.GetElement(host.GetTypeId())?.Name ?? "?", layers.Count, measured.Count, gap,
            [.. structure?.Conditions.Select(condition => condition.ToString()) ?? []], notes, fault);
    }

    /// <summary>
    /// The Wall Closure of each insert type hosted by a wall in force, by its
    /// stored value and the text Revit shows for it: which value is "By host"
    /// decides whether an insert overrides its wall type's wrapping.
    /// </summary>
    private static Dictionary<string, int> WallClosures(Document document, List<(HostObject Host, string Key)> hosts) =>
        hosts.Where(entry => entry.Host is Wall)
            .SelectMany(entry => entry.Host.FindInserts(true, false, false, false))
            .Select(id => document.GetElement(id))
            .OfType<FamilyInstance>()
            .Select(insert => insert.Symbol?.get_Parameter(BuiltInParameter.TYPE_WALL_CLOSURE))
            .Select(closure => closure is null ? "none" : $"{closure.AsInteger()} '{closure.AsValueString()}'")
            .GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());

    /// <summary>
    /// Sets a Keynote on the first measured material, reads it back through
    /// the reader, and rolls back: proof the Keynote read works where no
    /// sample carries one.
    /// </summary>
    private static string? KeynoteControl(Document document, List<(HostObject Host, string Key)> hosts)
    {
        foreach ((HostObject host, _) in hosts)
        {
            if (host.GetMaterialIds(false).Select(id => document.GetElement(id)).OfType<Material>().FirstOrDefault() is not Material material)
            {
                continue;
            }

            using Transaction transaction = new(document, "Metrado probe (rolled back)");
            transaction.Start();
            try
            {
                if (material.get_Parameter(BuiltInParameter.KEYNOTE_PARAM) is not { IsReadOnly: false } keynote || !keynote.Set("METRADO-PROBE"))
                {
                    return $"could not set the Keynote of {material.Name}";
                }

                LayerReading? reading = MaterialLayerReader.Read(document, host, null);
                string? read = reading?.Materials.FirstOrDefault(entry => entry.UniqueId == material.UniqueId)?.Keynote;
                return $"{material.Name} on {host.UniqueId}: set METRADO-PROBE, read back '{read}'";
            }
            finally
            {
                transaction.RollBack();
            }
        }

        return "no material to test";
    }
}
