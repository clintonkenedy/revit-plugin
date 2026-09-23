using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Metrado.Domain;
using RevitApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Metrado.Revit2027;

/// <summary>
/// Reads the model's floors and roofs into <see cref="HostReading"/>s: the
/// Revit-bound half of their extraction. Like <see cref="WallReader"/> it only
/// reads and opens no transaction; <see cref="SurfaceOpeningPolicy"/> decides
/// which openings are measured by the hole they leave in the upper faces.
/// </summary>
public static class SurfaceReader
{
    /// <summary>A face whose normal rises more than about 6 degrees above the horizontal faces up.</summary>
    private const double UpFacing = 0.1;

    public static IReadOnlyList<HostReading> ReadAll(Document document, Guid? sharedParameter = null) =>
    [
        .. Surfaces(document, BuiltInCategory.OST_Floors).Select(floor => Read(document, floor, HostTakeoff.FloorsKey, sharedParameter)),
        .. Surfaces(document, BuiltInCategory.OST_Roofs).Select(roof => Read(document, roof, HostTakeoff.RoofsKey, sharedParameter)),
    ];

    /// <summary>
    /// System floors and roofs, primary design option or none, as for walls.
    /// In-place ones are family instances with no computed area, outside this
    /// increment like curtain walls.
    /// </summary>
    public static IEnumerable<HostObject> Surfaces(Document document, BuiltInCategory category) =>
        new FilteredElementCollector(document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .OfType<HostObject>()
            .Where(host => host.DesignOption is not { IsPrimary: false });

    /// <summary>One warning per in-place floor or roof, primary design option or none, which no reading measures.</summary>
    public static IReadOnlyList<ValidationWarning> Unread(Document document) =>
    [
        .. InPlace(document, BuiltInCategory.OST_Floors, HostTakeoff.FloorsKey),
        .. InPlace(document, BuiltInCategory.OST_Roofs, HostTakeoff.RoofsKey),
    ];

    private static IEnumerable<ValidationWarning> InPlace(Document document, BuiltInCategory category, string key) =>
        new FilteredElementCollector(document)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Where(instance => instance.DesignOption is not { IsPrimary: false } && instance.SuperComponent is null)
            .Select(instance => HostTakeoff.NotRead(
                instance.UniqueId,
                key,
                instance.Symbol?.FamilyName is { Length: > 0 } family ? family : key,
                instance.Name is { Length: > 0 } name ? name : "(unnamed type)"));

    public static HostReading Read(Document document, HostObject host, string categoryKey, Guid? sharedParameter = null)
    {
        Element? type = document.GetElement(host.GetTypeId());
        (SurfaceFacts surface, IReadOnlyList<CutterFacts> cutters) = Facts(document, host);
        OpeningDecision openings = SurfaceOpeningPolicy.Decide(surface, cutters);

        return new HostReading(
            UniqueId: host.UniqueId,
            CategoryKey: categoryKey,
            FamilyName: (type as ElementType)?.FamilyName is { Length: > 0 } family ? family : categoryKey,
            TypeName: type?.Name is { Length: > 0 } name ? name : "(unnamed type)",
            TypeUniqueId: type?.UniqueId ?? host.UniqueId,
            AssemblyCode: OtherElementReader.Text(type, BuiltInParameter.ASSEMBLY_CODE),
            ComputedAreaSquareFeet: surface.ComputedSquareFeet,
            Openings: openings.Measured,
            Unmeasured: openings.Unmeasured,
            Keynote: OtherElementReader.Text(type, BuiltInParameter.KEYNOTE_PARAM),
            SharedParameters: OtherElementReader.Shared(host, type, sharedParameter));
    }

    /// <summary>What the policy judges: the upper faces, the holes in them, and every element cutting the host.</summary>
    public static (SurfaceFacts Surface, IReadOnlyList<CutterFacts> Cutters) Facts(Document document, HostObject host)
    {
        List<Face> faces = [.. Solids(host).SelectMany(solid => solid.Faces.Cast<Face>())];
        List<List<ElementId>> generators = [.. faces.Select(face => Generators(document, host, face).ToList())];
        List<ElementId> named = [.. generators.SelectMany(ids => ids).Distinct()];
        HashSet<ElementId> voids = [.. InstanceVoidCutUtils.GetCuttingVoidInstances(host)];

        List<PlanarFace> upper = [.. faces.OfType<PlanarFace>().Where(face => face.FaceNormal.Z > UpFacing)];
        List<LoopFacts> loops = [];
        foreach (PlanarFace face in upper)
        {
            foreach (EdgeArray edges in face.EdgeLoops.Cast<EdgeArray>())
            {
                (bool outer, double area) = Loop(face, edges);
                loops.Add(new LoopFacts(face.Id, outer, area, [.. edges.Cast<Edge>()
                    .SelectMany(edge => new[] { edge.GetFace(0), edge.GetFace(1) })
                    .OfType<Face>()
                    .Select(neighbour => neighbour.Id)
                    .Where(id => id != face.Id)
                    .Distinct()]));
            }
        }

        (IReadOnlyList<HoleFacts> holes, IReadOnlyList<CutterFacts> cutters) = SurfaceTopology.Assemble(
            [.. faces.Select((face, index) => new FaceFacts(
                face.Id,
                [.. generators[index].Select(id => Name(document, id))],
                face is PlanarFace { FaceNormal.Z: > UpFacing } ? face.Area : null))],
            loops,
            named.ToDictionary(id => Name(document, id), id => Kind(host, document.GetElement(id), voids)),
            // An opening element cuts its host whatever the faces say: on the
            // Snowdon sample one removed 11.4 m2 from a wall naming no face.
            [.. host.FindInserts(true, false, false, false).Where(id => document.GetElement(id) is Opening).Select(id => Name(document, id))]);

        double? computed = host.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED) is
            { StorageType: StorageType.Double, HasValue: true } parameter && double.IsFinite(parameter.AsDouble())
            ? parameter.AsDouble()
            : null;

        return (new SurfaceFacts(host.UniqueId, computed, upper.Sum(face => face.Area), holes), cutters);
    }

    /// <summary>
    /// The elements other than the host that generated the face. An opening's
    /// sketch stands for the opening, and the host's own sketch for the host.
    /// </summary>
    private static IEnumerable<ElementId> Generators(Document document, HostObject host, Face face) =>
        host.GetGeneratingElementIds(face)
            .Select(id => document.GetElement(id) is Sketch sketch ? sketch.OwnerId : id)
            .Where(id => id != host.Id && id != ElementId.InvalidElementId)
            .Distinct();

    private static CutterKind Kind(HostObject host, Element? cutter, HashSet<ElementId> voids) => cutter switch
    {
        Opening => CutterKind.Opening,
        FamilyInstance when voids.Contains(cutter.Id) => CutterKind.VoidCut,
        FamilyInstance { Host: { } hostOf } when hostOf.Id == host.Id => CutterKind.HostedInsert,
        _ => CutterKind.Joined,
    };

    /// <summary>
    /// Whether the loop is the face's outer boundary — counterclockwise about
    /// its normal — and the area it encloses. A loop that cannot be read is a
    /// hole of unknown area, so whatever it bounds is reported, never measured.
    /// </summary>
    private static (bool Outer, double Area) Loop(PlanarFace face, EdgeArray edges)
    {
        try
        {
            CurveLoop loop = CurveLoop.Create([.. edges.Cast<Edge>().Select(edge => edge.AsCurveFollowingFace(face))]);
            return (loop.IsCounterclockwise(face.FaceNormal), ExporterIFCUtils.ComputeAreaOfCurveLoops([loop]));
        }
        catch (RevitApplicationException)
        {
            return (false, double.NaN);
        }
    }

    private static string Name(Document document, ElementId id) => document.GetElement(id)?.UniqueId ?? id.ToString();

    private static IEnumerable<Solid> Solids(Element element) =>
        element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })?
            .OfType<Solid>()
            .Where(solid => solid.Faces.Size > 0) ?? [];
}
