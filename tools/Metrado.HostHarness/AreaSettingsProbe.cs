using Autodesk.Revit.DB;
using Metrado.Revit2027;

namespace Metrado.HostHarness;

/// <summary>
/// Answers the design's first open question on a real model: does the
/// project's Area and Volume Computations setting gate the quantity the
/// Walls criterion reads, HOST_AREA_COMPUTED? The setting is flipped in a
/// transaction that is rolled back, the document regenerated, and every wall
/// Metrado reads is read under both settings. The model ends exactly as it
/// started and is never saved — which is why this lives in the harness and
/// never in Metrado.
/// </summary>
public static class AreaSettingsProbe
{
    /// <param name="AreasThatDiffer">Walls whose area was not the same under both settings (a value appearing or vanishing counts).</param>
    public sealed record Result(int Walls, Reading AsFound, Reading Flipped, int AreasThatDiffer, bool ModifiedAfterProbe);

    public sealed record Reading(bool ComputeVolumes, int AreasWithValue, int VolumesWithValue, double AreaSumSquareFeet);

    public static Result Run(Document document)
    {
        List<Wall> walls = [.. WallReader.Walls(document)];
        bool asFound = AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes;

        Dictionary<ElementId, double?> areasAsFound = Areas(walls);
        Reading readingAsFound = Read(asFound, walls, areasAsFound);

        Dictionary<ElementId, double?> areasFlipped;
        Reading readingFlipped;
        using (Transaction transaction = new(document, "Metrado probe (rolled back)"))
        {
            transaction.Start();
            try
            {
                AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes = !asFound;
                document.Regenerate();
                areasFlipped = Areas(walls);
                readingFlipped = Read(!asFound, walls, areasFlipped);
            }
            finally
            {
                transaction.RollBack();
            }
        }

        int differ = walls.Count(wall => areasAsFound[wall.Id] is not { } a || areasFlipped[wall.Id] is not { } b
            ? areasAsFound[wall.Id].HasValue != areasFlipped[wall.Id].HasValue
            : Math.Abs(a - b) > 1e-9);
        return new Result(walls.Count, readingAsFound, readingFlipped, differ, document.IsModified);
    }

    private static Dictionary<ElementId, double?> Areas(List<Wall> walls) =>
        walls.ToDictionary(wall => wall.Id, wall => Value(wall, BuiltInParameter.HOST_AREA_COMPUTED));

    private static Reading Read(bool computeVolumes, List<Wall> walls, Dictionary<ElementId, double?> areas) =>
        new(computeVolumes,
            areas.Values.Count(area => area.HasValue),
            walls.Count(wall => Value(wall, BuiltInParameter.HOST_VOLUME_COMPUTED).HasValue),
            areas.Values.Sum(area => area ?? 0));

    private static double? Value(Element element, BuiltInParameter id) =>
        element.get_Parameter(id) is { HasValue: true, StorageType: StorageType.Double } parameter ? parameter.AsDouble() : null;
}
