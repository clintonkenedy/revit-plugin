using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using RevitApplicationException = Autodesk.Revit.Exceptions.ApplicationException;
using RevitModificationForbidden = Autodesk.Revit.Exceptions.ModificationForbiddenException;
using RevitModificationOutsideTransaction = Autodesk.Revit.Exceptions.ModificationOutsideTransactionException;

namespace Metrado.Revit2027;

/// <summary>
/// Reads the model's walls into <see cref="WallReading"/>s: the Revit-bound
/// half of wall extraction. It only reads — collectors, parameters, geometry
/// and outlines — and opens no transaction; <see cref="OpeningPolicy"/> makes
/// every decision about what a reading carries.
///
/// Parameters are read by <see cref="BuiltInParameter"/>, never by the names
/// the UI shows, so a Spanish session reads what an English one reads.
/// </summary>
public static class WallReader
{
    public static IReadOnlyList<WallReading> ReadAll(Document document) =>
        [.. Walls(document).Select(wall => Read(document, wall))];

    /// <summary>
    /// Basic walls only, primary design option or none. Curtain and stacked
    /// walls follow other rules and are outside the first increment; their
    /// members, being basic walls, are read.
    /// </summary>
    public static IEnumerable<Wall> Walls(Document document) =>
        new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .OfType<Wall>()
            .Where(wall => wall.WallType.Kind == WallKind.Basic && wall.DesignOption is not { IsPrimary: false });

    public static WallReading Read(Document document, Wall wall)
    {
        WallType type = wall.WallType;
        Axis? axis = wall.Location is LocationCurve { Curve: Line line }
            ? new Axis(line.GetEndPoint(0), line.Direction)
            : null;
        HashSet<ElementId> cuts = CutEvidence(wall);

        OpeningDecision openings = OpeningPolicy.Decide(
            new WallFacts(axis is null ? Unknown : Extent(axis, SolidPoints(wall)), Conditions(document, wall, axis, cuts)),
            Inserts(document, wall, axis, cuts));

        return new WallReading(
            UniqueId: wall.UniqueId,
            FamilyName: NonBlank(type.FamilyName, type.Kind.ToString()),
            TypeName: NonBlank(type.Name, "(unnamed type)"),
            TypeUniqueId: type.UniqueId,
            AssemblyCode: type.get_Parameter(BuiltInParameter.ASSEMBLY_CODE) is { StorageType: StorageType.String } code
                ? code.AsString()
                : null,
            ComputedAreaSquareFeet: wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED) is
                { StorageType: StorageType.Double, HasValue: true } area && double.IsFinite(area.AsDouble())
                ? area.AsDouble()
                : null,
            Openings: openings.Measured,
            Unmeasured: openings.Unmeasured);
    }

    private sealed record Axis(XYZ Origin, XYZ Direction);

    private static readonly Box Unknown = new(double.NaN, double.NaN, double.NaN, double.NaN);

    /// <summary>Conditions under which an opening's outline stops matching what Revit deducts.</summary>
    private static List<string> Conditions(Document document, Wall wall, Axis? axis, HashSet<ElementId> cuts)
    {
        List<string> conditions = [];
        CompoundStructure? structure = wall.WallType.GetCompoundStructure();

        if (axis is null)
        {
            conditions.Add("it is not straight");
        }

        if (structure is not null
            && (structure.GetWallSweepsInfo(WallSweepType.Sweep).Count > 0 || structure.GetWallSweepsInfo(WallSweepType.Reveal).Count > 0))
        {
            conditions.Add("its type carries sweeps or reveals");
        }

        if (cuts.Any(id => document.GetElement(id) is WallSweep))
        {
            conditions.Add("a sweep or reveal runs across it");
        }

        if (ExporterIFCUtils.HasElevationProfile(wall))
        {
            conditions.Add("its profile is edited");
        }

        // An attached top or base is deliberately not a condition. On the
        // Pacific sample 68 of 71 outlines on attached walls matched Revit's
        // deduction exactly, 2 fell short by 1e-4 m2, and the one that
        // overstated (by 4.88 m2) reached 2.13 m beyond the wall solid, which
        // the containment check already reports.
        return conditions;
    }

    private static List<InsertFacts> Inserts(Document document, Wall wall, Axis? axis, HashSet<ElementId> cuts)
    {
        HashSet<ElementId> voids = [.. InstanceVoidCutUtils.GetCuttingVoidInstances(wall)];
        List<InsertFacts> facts = [];

        foreach (ElementId id in wall.FindInserts(true, true, true, true).Union(voids))
        {
            Element insert = document.GetElement(id);
            InsertKind kind = insert switch
            {
                FamilyInstance => InsertKind.FamilyInstance,
                Opening { IsRectBoundary: true } => InsertKind.RectangularOpening,
                Wall => InsertKind.EmbeddedWall,
                _ => InsertKind.Other,
            };
            bool hosted = insert switch
            {
                FamilyInstance instance => instance.Host?.Id == wall.Id,
                Opening opening => opening.Host?.Id == wall.Id,
                _ => false,
            };
            // Opening elements are counted as cuts whatever the face evidence
            // says: on the Snowdon sample one removed 11.4 m2 without being
            // named as the generator of any of the wall's faces.
            bool cutsWall = cuts.Contains(id) || insert is Opening;

            (double? area, string? error, Box? outline) = cutsWall && axis is not null
                ? Outline(document, wall, insert, axis)
                : (null, null, null);
            facts.Add(new InsertFacts(insert.UniqueId, kind, cutsWall, hosted, voids.Contains(id), area, error, outline));
        }

        return facts;
    }

    /// <summary>
    /// The insert's own outline and its area where Revit can give one;
    /// otherwise, for judging overlaps only, a range known to contain it.
    /// </summary>
    private static (double? Area, string? Error, Box? Outline) Outline(Document document, Wall wall, Element insert, Axis axis)
    {
        try
        {
            switch (insert)
            {
                case FamilyInstance instance:
                    CurveLoop loop = ExporterIFCUtils.GetInstanceCutoutFromWall(document, wall, instance, out XYZ _);
                    return loop is null
                        ? (null, "no outline was returned", Bounds(axis, insert))
                        : (ExporterIFCUtils.ComputeAreaOfCurveLoops([loop]), null,
                            Extent(axis, loop.SelectMany(curve => curve.Tessellate())));

                case Opening { IsRectBoundary: true } opening:
                    XYZ a = opening.BoundaryRect[0], b = opening.BoundaryRect[1];
                    return (new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() * Math.Abs(b.Z - a.Z), null, Extent(axis, [a, b]));

                case Wall embedded:
                    return (null, null, Extent(axis, SolidPoints(embedded)) is { UMin: not double.NaN } extent ? extent : Bounds(axis, insert));

                default:
                    return (null, null, Bounds(axis, insert));
            }
        }
        catch (RevitApplicationException ex) when (ex is not RevitModificationForbidden and not RevitModificationOutsideTransaction)
        {
            // "Couldn't generate cut-out." for families without an opening
            // cut, among others. A write attempt is not caught: it must fail
            // the run, because the command promised to change nothing.
            return (null, ex.Message, Bounds(axis, insert));
        }
    }

    /// <summary>The element's bounding box projected on the wall: coarse, but it contains the element's cut.</summary>
    private static Box? Bounds(Axis axis, Element element)
    {
        if (element.get_BoundingBox(null) is not BoundingBoxXYZ box)
        {
            return null;
        }

        // All eight corners in the box's own frame, then transformed: taking
        // only a transformed min and max would not contain a rotated box.
        XYZ min = box.Min, max = box.Max;
        return Extent(axis,
            from x in new[] { min.X, max.X }
            from y in new[] { min.Y, max.Y }
            from z in new[] { min.Z, max.Z }
            select box.Transform.OfPoint(new XYZ(x, y, z)));
    }

    /// <summary>Ids of the elements that generated this wall's faces: the inserts that actually cut it.</summary>
    private static HashSet<ElementId> CutEvidence(Wall wall)
    {
        HashSet<ElementId> ids = [];
        foreach (Face face in Solids(wall).SelectMany(solid => solid.Faces.Cast<Face>()))
        {
            ids.UnionWith(wall.GetGeneratingElementIds(face));
        }

        ids.Remove(wall.Id);
        return ids;
    }

    private static IEnumerable<Solid> Solids(Wall wall) =>
        wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })?
            .OfType<Solid>()
            .Where(solid => solid.Faces.Size > 0) ?? [];

    private static IEnumerable<XYZ> SolidPoints(Wall wall) =>
        Solids(wall).SelectMany(solid => solid.Edges.Cast<Edge>()).SelectMany(edge => edge.Tessellate());

    private static Box Extent(Axis axis, IEnumerable<XYZ> points)
    {
        List<(double U, double Z)> projected = [.. points.Select(p => (axis.Direction.DotProduct(p - axis.Origin), p.Z))];
        return projected.Count == 0
            ? Unknown
            : new Box(projected.Min(p => p.U), projected.Max(p => p.U), projected.Min(p => p.Z), projected.Max(p => p.Z));
    }

    private static string NonBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
