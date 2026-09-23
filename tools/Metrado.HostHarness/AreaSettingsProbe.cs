using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Metrado.Revit2027;

namespace Metrado.HostHarness;

/// <summary>
/// Answers the design's first open question on a real model: does the
/// project's Area and Volume Computations setting gate the quantity the
/// Walls criterion reads, HOST_AREA_COMPUTED? Both of the dialog's settings
/// are changed in a transaction that is rolled back, the document
/// regenerated after each: volume computation flipped, then the room area
/// boundary moved. Each setting is read back after it is set, and the rooms
/// are read too, as the positive control: a change the rooms show and the
/// walls do not is the answer, while one nothing shows proves nothing. The
/// model ends exactly as it started and is never saved — which is why this
/// lives in the harness and never in Metrado.
/// </summary>
public static class AreaSettingsProbe
{
    public sealed record Result(int Walls, int Rooms, List<Reading> Readings, bool ModifiedAfterProbe);

    /// <param name="State">What was set, as read back from the document.</param>
    /// <param name="WallAreasThatDiffer">Walls whose HOST_AREA_COMPUTED is not the one read as found (a value appearing or vanishing counts).</param>
    /// <param name="WallVolumesThatDiffer">The same for HOST_VOLUME_COMPUTED.</param>
    public sealed record Reading(
        string State, bool ComputeVolumes, string RoomBoundary,
        int WallAreasWithValue, double WallAreaSumSquareFeet, int WallAreasThatDiffer, int WallVolumesThatDiffer,
        int RoomsWithVolume, double RoomVolumeSumCubicFeet, double RoomAreaSumSquareFeet);

    public static Result Run(Document document)
    {
        List<Wall> walls = [.. WallReader.Walls(document)];
        List<Room> rooms = [.. new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>()];
        AreaVolumeSettings asFound = AreaVolumeSettings.GetAreaVolumeSettings(document);
        SpatialElementBoundaryLocation boundary = asFound.GetSpatialElementBoundaryLocation(SpatialElementType.Room);

        Snapshot found = Snapshot.Of(walls);
        List<Reading> readings = [Read("as found", document, walls, rooms, found)];

        using (Transaction transaction = new(document, "Metrado probe (rolled back)"))
        {
            transaction.Start();
            try
            {
                AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes = !asFound.ComputeVolumes;
                document.Regenerate();
                readings.Add(Read("volume computation flipped", document, walls, rooms, found));

                AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes = asFound.ComputeVolumes;
                AreaVolumeSettings.GetAreaVolumeSettings(document).SetSpatialElementBoundaryLocation(
                    boundary == SpatialElementBoundaryLocation.Center ? SpatialElementBoundaryLocation.Finish : SpatialElementBoundaryLocation.Center,
                    SpatialElementType.Room);
                document.Regenerate();
                readings.Add(Read("room area boundary moved", document, walls, rooms, found));
            }
            finally
            {
                transaction.RollBack();
            }
        }

        return new Result(walls.Count, rooms.Count, readings, document.IsModified);
    }

    private static Reading Read(string state, Document document, List<Wall> walls, List<Room> rooms, Snapshot found)
    {
        AreaVolumeSettings settings = AreaVolumeSettings.GetAreaVolumeSettings(document);
        Snapshot now = Snapshot.Of(walls);
        return new Reading(
            state,
            settings.ComputeVolumes,
            settings.GetSpatialElementBoundaryLocation(SpatialElementType.Room).ToString(),
            now.Areas.Count(area => area.HasValue),
            now.Areas.Sum(area => area ?? 0),
            Differ(found.Areas, now.Areas),
            Differ(found.Volumes, now.Volumes),
            rooms.Count(room => room.Volume > 0),
            rooms.Sum(room => room.Volume),
            rooms.Sum(room => room.Area));
    }

    private static int Differ(double?[] before, double?[] after) =>
        before.Zip(after).Count(pair => pair.First is not { } a || pair.Second is not { } b
            ? pair.First.HasValue != pair.Second.HasValue
            : Math.Abs(a - b) > 1e-9);

    private static double? Value(Element element, BuiltInParameter id) =>
        element.get_Parameter(id) is { HasValue: true, StorageType: StorageType.Double } parameter ? parameter.AsDouble() : null;

    private sealed record Snapshot(double?[] Areas, double?[] Volumes)
    {
        public static Snapshot Of(List<Wall> walls) =>
            new([.. walls.Select(wall => Value(wall, BuiltInParameter.HOST_AREA_COMPUTED))],
                [.. walls.Select(wall => Value(wall, BuiltInParameter.HOST_VOLUME_COMPUTED))]);
    }
}
