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
        Dictionary<ElementId, List<XYZ>> generators = FaceGenerators(wall);
        HashSet<ElementId> cuts = [.. generators.Keys];
        HashSet<ElementId> voids = [.. InstanceVoidCutUtils.GetCuttingVoidInstances(wall)];
        List<ElementId> candidates = [.. wall.FindInserts(true, true, true, true).Union(voids)];

        // Joined elements that are not inserts — floors, beams, columns —
        // cut the wall too; where, is where the faces they generate lie.
        List<Box> otherCuts = axis is null
            ? []
            : [.. generators
                .Where(entry => !candidates.Contains(entry.Key) && document.GetElement(entry.Key) is not WallSweep)
                .Select(entry => Extent(axis, entry.Value))];

        OpeningDecision openings = OpeningPolicy.Decide(
            new WallFacts(axis is null ? Unknown : Extent(axis, SolidPoints(wall)), Conditions(document, wall, axis, cuts), otherCuts),
            Inserts(document, wall, axis, cuts, voids, candidates));

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

        if (wall.CrossSection != WallCrossSection.Vertical)
        {
            conditions.Add("it is slanted or tapered");
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

        // Containment only compares ranges, so a wall whose elevation is not a
        // rectangle — a top attached to a pitched roof, a base on a sloped
        // floor — can hold an outline inside its range that crosses its real
        // edge. Most outlines on attached walls were exact on the Pacific
        // sample, but a gable would not be.
        if (wall.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger() == 1
            || wall.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger() == 1)
        {
            conditions.Add("its top or base is attached, so its edge may not be straight");
        }

        return conditions;
    }

    private static List<InsertFacts> Inserts(
        Document document, Wall wall, Axis? axis, HashSet<ElementId> cuts, HashSet<ElementId> voids, List<ElementId> candidates)
    {
        List<InsertFacts> facts = [];

        foreach (ElementId id in candidates)
        {
            Element insert = document.GetElement(id);

            // A secondary design option is not the model being measured: its
            // inserts deduct nothing from the primary wall, just as its walls
            // are not read. Counting one as a cut would add back what was
            // never removed.
            if (insert.DesignOption is { IsPrimary: false })
            {
                continue;
            }
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
                    // Width along the wall, not the corners' plan distance,
                    // which would add any offset across the wall's thickness.
                    Box rect = Extent(axis, [opening.BoundaryRect[0], opening.BoundaryRect[1]]);
                    return ((rect.UMax - rect.UMin) * (rect.ZMax - rect.ZMin), null, rect);

                case Wall embedded:
                    return (null, null, Extent(axis, SolidPoints(embedded)) is { UMin: not double.NaN } extent ? extent : Bounds(axis, insert));

                default:
                    return (null, null, Bounds(axis, insert));
            }
        }
        catch (RevitApplicationException ex) when (!IsWriteRefusal(ex))
        {
            // "Couldn't generate cut-out." for families without an opening
            // cut, among others. A write attempt is not caught: it must fail
            // the run, because the command promised to change nothing.
            return (null, ex.Message, Bounds(axis, insert));
        }
    }

    /// <summary>
    /// A write attempted during a read-only command. Besides the two
    /// modification exceptions, Revit refuses a transaction in a read-only
    /// command with its base InvalidOperationException and this message.
    /// </summary>
    private static bool IsWriteRefusal(RevitApplicationException ex) =>
        ex is RevitModificationForbidden or RevitModificationOutsideTransaction
        || ex.Message.Contains("Cannot modify the document", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every element that generated faces of this wall, with the points of
    /// those faces: the inserts that actually cut it, and the joined elements
    /// whose cuts are located by where their faces lie.
    /// </summary>
    private static Dictionary<ElementId, List<XYZ>> FaceGenerators(Wall wall)
    {
        Dictionary<ElementId, List<XYZ>> generators = [];
        foreach (Face face in Solids(wall).SelectMany(solid => solid.Faces.Cast<Face>()))
        {
            List<XYZ>? points = null;
            foreach (ElementId id in wall.GetGeneratingElementIds(face).Where(id => id != wall.Id))
            {
                points ??= [.. face.GetEdgesAsCurveLoops().SelectMany(loop => loop).SelectMany(curve => curve.Tessellate())];
                if (!generators.TryGetValue(id, out List<XYZ>? known))
                {
                    generators[id] = known = [];
                }

                known.AddRange(points);
            }
        }

        return generators;
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
