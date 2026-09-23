using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;

namespace Metrado.HostHarness;

/// <summary>
/// Measures, on a real model, how far each candidate way of reading an
/// opening's area is from what Revit actually subtracted.
///
/// Ground truth needs writes: each insert is deleted in its own transaction,
/// the document regenerated, the wall's HOST_AREA_COMPUTED read, and the
/// transaction rolled back — so the model ends exactly as it started, and it
/// is never saved. That is why this lives in the harness and never in
/// Metrado, whose commands are read-only.
/// </summary>
public static class WallProbe
{
    public sealed record Result(
        double SquareMetresPerSquareFoot,
        double Converted107_639,
        int BasicStraightWallsWithInserts,
        List<WallResult> Walls,
        bool ModifiedAfterProbe);

    /// <param name="Conditions">Wall conditions a runtime check could see, recorded to learn which ones predict a wrong cutout.</param>
    /// <param name="Metrado">What Metrado's own WallReader decided for this wall, judged against the ground truth.</param>
    public sealed record WallResult(
        string UniqueId, string TypeName, double LengthM, double HeightM, double WidthM,
        double AreaM2, double GrossM2, double CutoutSumM2, double ResidualM2,
        WallConditions Conditions, List<InsertResult> Inserts, MetradoVerdict Metrado);

    /// <param name="Measured">Each opening Metrado measured, with the ground truth it should equal.</param>
    /// <param name="Reported">Each opening Metrado reported instead, with the area Revit really deducted for it.</param>
    /// <param name="IgnoredButDeducting">Inserts Revit deducted area for that Metrado neither measured nor reported.</param>
    public sealed record MetradoVerdict(
        double? ComputedAreaM2,
        List<MeasuredVerdict> Measured,
        List<ReportedVerdict> Reported,
        List<string> IgnoredButDeducting);

    public sealed record MeasuredVerdict(string UniqueId, double MeasuredM2, double DeltaM2, double DifferenceM2);

    public sealed record ReportedVerdict(string UniqueId, string Reason, double DeltaM2);

    public sealed record WallConditions(
        int TypeSweeps, int TypeReveals, bool HasElevationProfile, bool TopAttached, bool BaseAttached,
        int JoinedElements, int StandaloneSweepsOrReveals);

    /// <param name="CutoutM2">Method A's area, or null when it threw or gave nothing.</param>
    /// <param name="DeltaM2">Ground truth: how much the wall's area grows when this insert alone is deleted.</param>
    /// <param name="OverhangM">How far the cutout outline reaches beyond the wall solid, along the wall or vertically.</param>
    public sealed record InsertResult(
        string UniqueId, string Category, string Class, bool HostIsWall, bool InCutEvidence,
        double? CutoutM2, double? CutoutRaw, string? CutoutError, double DeltaM2, double? DifferenceM2,
        double? OverhangM);

    public static Result Run(Document document, int maxWalls)
    {
        // The same walls the ribbon command reads — Metrado's own filter,
        // stacked-wall members included — narrowed to straight ones with
        // inserts, the only ones whose outlines the probe can place.
        List<Wall> walls = [.. Metrado.Revit2027.WallReader.Walls(document)
            .Where(wall => wall.Location is LocationCurve { Curve: Line }
                && wall.FindInserts(true, true, true, true).Count > 0)];

        // Walls holding one door and two windows first: the spec's own scenario.
        List<WallResult> measured = [.. walls
            .OrderByDescending(wall => IsDoorAndTwoWindows(document, wall))
            .ThenByDescending(wall => wall.FindInserts(true, true, true, true).Count)
            .Take(maxWalls)
            .Select(wall => Measure(document, wall))];

        return new Result(
            SquareMetres(1.0),
            SquareMetres(107.639),
            walls.Count,
            measured,
            document.IsModified);
    }

    private static WallResult Measure(Document document, Wall wall)
    {
        // Metrado reads first, before any probe transaction touches the model.
        Metrado.Revit2027.WallReading reading = Metrado.Revit2027.WallReader.Read(document, wall);
        double area = Area(wall);
        List<ElementId> inserts = [.. wall.FindInserts(true, true, true, true)
            .Union(InstanceVoidCutUtils.GetCuttingVoidInstances(wall))];
        HashSet<ElementId> evidence = CutEvidence(wall);

        // Copied out before any transaction: a rolled-back transaction leaves
        // earlier geometry proxies dangling, and touching one crashes natively.
        Line line = (Line)((LocationCurve)wall.Location).Curve;
        Axis axis = new(line.GetEndPoint(0), line.Direction, line.Length);
        (double uMin, double uMax, double zMin, double zMax) extent = Extent(axis, SolidPoints(wall));

        List<InsertResult> results = [.. inserts.Select(id =>
        {
            Element insert = document.GetElement(id);
            (double? cutout, double? raw, string? error, List<XYZ> outline) = Cutout(document, wall, insert);
            // NaN when Revit refused the deletion: never converted (UnitUtils
            // throws on it) and never counted as "deducts nothing".
            double without = AreaWithout(document, wall, [id]);
            double delta = double.IsFinite(without) ? SquareMetres(without - area) : double.NaN;
            return new InsertResult(
                insert.UniqueId,
                insert.Category?.BuiltInCategory.ToString() ?? "none",
                insert.GetType().Name,
                insert is FamilyInstance { Host: not null } fi && fi.Host.Id == wall.Id,
                evidence.Contains(id),
                cutout, raw, error, delta,
                cutout - delta,
                outline.Count == 0 ? null : Metres(Overhang(axis, extent, outline)));
        })];

        CompoundStructure? structure = wall.WallType.GetCompoundStructure();
        WallConditions conditions = new(
            structure?.GetWallSweepsInfo(WallSweepType.Sweep).Count ?? 0,
            structure?.GetWallSweepsInfo(WallSweepType.Reveal).Count ?? 0,
            ExporterIFCUtils.HasElevationProfile(wall),
            wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() == 1,
            wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() == 1,
            JoinGeometryUtils.GetJoinedElements(document, wall).Count,
            evidence.Count(id => document.GetElement(id) is WallSweep));

        Dictionary<string, double> truth = results.ToDictionary(result => result.UniqueId, result => result.DeltaM2);
        double Truth(string uniqueId) => truth.TryGetValue(uniqueId, out double delta) ? delta : double.NaN;
        MetradoVerdict verdict = new(
            reading.ComputedAreaSquareFeet is double computed ? SquareMetres(computed) : null,
            [.. reading.Openings.Select(opening => new MeasuredVerdict(
                opening.UniqueId, SquareMetres(opening.AreaSquareFeet), Truth(opening.UniqueId),
                SquareMetres(opening.AreaSquareFeet) - Truth(opening.UniqueId)))],
            [.. reading.Unmeasured.Select(opening => new ReportedVerdict(opening.UniqueId, opening.Reason, Truth(opening.UniqueId)))],
            [.. results
                .Where(result => !(Math.Abs(result.DeltaM2) <= 1e-6)
                    && !reading.Openings.Any(opening => opening.UniqueId == result.UniqueId)
                    && !reading.Unmeasured.Any(opening => opening.UniqueId == result.UniqueId))
                .Select(result => $"{result.UniqueId} ({result.Category}, {result.DeltaM2:0.######} m2)")]);

        double grossFeet = AreaWithout(document, wall, inserts);
        double gross = double.IsFinite(grossFeet) ? grossFeet : double.NaN;
        double cutoutSum = results.Sum(result => result.CutoutM2 ?? 0);
        return new WallResult(
            wall.UniqueId,
            wall.WallType.Name,
            Metres(axis.Length),
            Metres(wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? double.NaN),
            Metres(wall.Width),
            FiniteSquareMetres(area),
            FiniteSquareMetres(gross),
            cutoutSum,
            FiniteSquareMetres(area) + cutoutSum - FiniteSquareMetres(gross),
            conditions,
            results,
            verdict);
    }

    private static (double? Area, double? Raw, string? Error, List<XYZ> Outline) Cutout(
        Document document, Wall wall, Element insert)
    {
        try
        {
            switch (insert)
            {
                case FamilyInstance instance:
                    CurveLoop loop = ExporterIFCUtils.GetInstanceCutoutFromWall(document, wall, instance, out XYZ _);
                    if (loop is null)
                    {
                        return (null, null, "null loop", []);
                    }

                    double raw = ExporterIFCUtils.ComputeAreaOfCurveLoops([loop]);
                    List<XYZ> outline = [.. loop.SelectMany(curve => curve.Tessellate())];
                    return (SquareMetres(Math.Abs(raw)), raw, raw > 0 ? null : "non-positive area", outline);

                case Opening { IsRectBoundary: true } opening:
                    IList<XYZ> corners = opening.BoundaryRect;
                    XYZ a = corners[0], b = corners[1];
                    double rect = new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() * Math.Abs(b.Z - a.Z);
                    return (SquareMetres(rect), rect, null, [a, b]);

                default:
                    return (null, null, $"no method for {insert.GetType().Name}", []);
            }
        }
        catch (Exception ex)
        {
            return (null, null, $"{ex.GetType().FullName}: {ex.Message}", []);
        }
    }

    private static IEnumerable<XYZ> SolidPoints(Wall wall) =>
        wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })?
            .OfType<Solid>()
            .SelectMany(solid => solid.Edges.Cast<Edge>())
            .SelectMany(edge => edge.Tessellate()) ?? [];

    private sealed record Axis(XYZ Origin, XYZ Direction, double Length);

    /// <summary>The wall solid's range along its axis (u) and in height (z), in feet.</summary>
    private static (double, double, double, double) Extent(Axis axis, IEnumerable<XYZ> points)
    {
        List<(double U, double Z)> projected = [.. points.Select(p => (axis.Direction.DotProduct(p - axis.Origin), p.Z))];
        return projected.Count == 0
            ? (double.NaN, double.NaN, double.NaN, double.NaN)
            : (projected.Min(p => p.U), projected.Max(p => p.U), projected.Min(p => p.Z), projected.Max(p => p.Z));
    }

    /// <summary>The largest distance, in feet, by which the outline leaves the wall's range.</summary>
    private static double Overhang(Axis axis, (double UMin, double UMax, double ZMin, double ZMax) wall, List<XYZ> outline)
    {
        (double uMin, double uMax, double zMin, double zMax) = Extent(axis, outline);
        return new[] { wall.UMin - uMin, uMax - wall.UMax, wall.ZMin - zMin, zMax - wall.ZMax, 0 }.Max();
    }

    /// <summary>NaN when Revit refuses the deletion (a grouped or pinned insert, for instance).</summary>
    private static double AreaWithout(Document document, Wall wall, IReadOnlyCollection<ElementId> inserts)
    {
        using Transaction transaction = new(document, "Metrado probe (rolled back)");
        transaction.Start();
        try
        {
            document.Delete(inserts.ToList());
            document.Regenerate();
            return Area(wall);
        }
        catch (Exception)
        {
            return double.NaN;
        }
        finally
        {
            transaction.RollBack();
        }
    }

    private static HashSet<ElementId> CutEvidence(Wall wall)
    {
        HashSet<ElementId> ids = [];
        GeometryElement? geometry = wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        foreach (Solid solid in geometry?.OfType<Solid>().Where(solid => solid.Faces.Size > 0) ?? [])
        {
            foreach (Face face in solid.Faces)
            {
                ids.UnionWith(wall.GetGeneratingElementIds(face));
            }
        }

        ids.Remove(wall.Id);
        return ids;
    }

    private static bool IsDoorAndTwoWindows(Document document, Wall wall)
    {
        List<BuiltInCategory> categories = [.. wall.FindInserts(false, false, false, false)
            .Select(id => document.GetElement(id).Category?.BuiltInCategory ?? BuiltInCategory.INVALID)];
        return categories.Count == 3
            && categories.Count(c => c == BuiltInCategory.OST_Doors) == 1
            && categories.Count(c => c == BuiltInCategory.OST_Windows) == 2;
    }

    private static double Area(Wall wall) =>
        wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? double.NaN;

    /// <summary>NaN stays NaN: UnitUtils throws on a non-finite value.</summary>
    private static double FiniteSquareMetres(double squareFeet) =>
        double.IsFinite(squareFeet) ? SquareMetres(squareFeet) : double.NaN;

    private static double SquareMetres(double squareFeet) =>
        UnitUtils.ConvertFromInternalUnits(squareFeet, UnitTypeId.SquareMeters);

    private static double Metres(double feet) =>
        double.IsFinite(feet) ? UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters) : double.NaN;
}
