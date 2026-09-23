using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;

namespace Metrado.HostHarness;

/// <summary>
/// Measures, on a real model, how floors and roofs lose area to their
/// openings (task 2.6's second half): the evidence the rule was written from,
/// and the check it is held to. For each host with holes or cutters it
/// records the up-facing faces' area against HOST_AREA_COMPUTED, every edge
/// loop of those faces (outer or a hole, by its orientation) with its area
/// and the elements generating the faces across its edges, every face each
/// cutter generates, the loops of a floor's own sketch, the elements whose
/// plan box meets a hole without naming any of its faces, and what the
/// add-in's own <c>SurfaceReader</c> measures and reports. As ground truth,
/// it deletes each cutter alone in a transaction that is rolled back and
/// reads how much the host's area grows; for a cutter giving back less than
/// its hole, it also reads the holes left behind and undoes each nearby join
/// the same way. The model ends exactly as it started and is never saved.
/// </summary>
public static class HostProbe
{
    public sealed record Result(List<HostResult> Hosts, bool ModifiedAfterProbe);

    /// <param name="TopFaceAreaM2">The up-facing faces' area, which should equal the computed area if those faces define it.</param>
    public sealed record HostResult(
        string Category, string UniqueId, string TypeName, double? AreaM2, double TopFaceAreaM2, int TopFaces,
        List<LoopResult> Loops, List<CutterResult> Cutters, List<double?> SketchLoopsM2, List<string> Hidden,
        List<string> Measured, List<string> Unmeasured);

    /// <param name="Outer">Counterclockwise about the face's normal: the face's outer boundary; otherwise a hole.</param>
    /// <param name="Generators">Elements other than the host generating the faces across the loop's edges.</param>
    public sealed record LoopResult(int Face, bool Outer, double? AreaM2, List<string> Generators, string? Error);

    /// <param name="DeltaM2">Ground truth: how much the computed area grows when this cutter alone is deleted.</param>
    /// <param name="WithoutJoins">
    /// For a cutter giving back less than its hole: per element joined to the
    /// host, how much deleting the cutter gives back once that join is undone
    /// too, net of what undoing the join alone gives back.
    /// </param>
    public sealed record CutterResult(string UniqueId, string Category, string Class, string? Owner, string? HostOf, List<FaceResult> Faces, double? DeltaM2, List<string> WithoutJoins);

    /// <param name="Across">The indexes of the up-facing loops this face meets across an edge.</param>
    public sealed record FaceResult(double NormalZ, double AreaM2, bool Upper, List<string> Across);

    private const double UpFacing = 0.1;

    public static Result Run(Document document, int maxHosts)
    {
        List<HostObject> hosts =
        [
            .. new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().OfType<HostObject>(),
            .. new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType().OfType<HostObject>(),
        ];

        List<HostResult> results = [];
        foreach (HostObject host in hosts)
        {
            HostResult found = Probe(document, host);
            if (found.Loops.Any(loop => !loop.Outer) || found.Cutters.Count > 0)
            {
                results.Add(found);
                if (results.Count >= maxHosts)
                {
                    break;
                }
            }
        }

        return new Result(results, document.IsModified);
    }

    private static HostResult Probe(Document document, HostObject host)
    {
        // What the add-in itself decides, read before anything is deleted.
        Metrado.Revit2027.HostReading reading = Metrado.Revit2027.SurfaceReader.Read(
            document, host, host is RoofBase ? Metrado.Revit2027.HostTakeoff.RoofsKey : Metrado.Revit2027.HostTakeoff.FloorsKey);

        List<Face> faces = [.. Solids(host).SelectMany(solid => solid.Faces.Cast<Face>())];
        List<PlanarFace> top = [.. faces.OfType<PlanarFace>().Where(face => face.FaceNormal.Z > UpFacing)];

        // Which up-facing loop each face meets across an edge, by face id.
        Dictionary<int, List<string>> across = [];
        List<LoopResult> loops = [];
        List<(string Name, XYZ Min, XYZ Max)> holeBoxes = [];
        for (int index = 0; index < top.Count; index++)
        {
            PlanarFace face = top[index];
            foreach (EdgeArray edges in face.EdgeLoops.Cast<EdgeArray>())
            {
                string name = $"{index}.{loops.Count}";
                HashSet<ElementId> generators = [];
                foreach (Edge edge in edges.Cast<Edge>())
                {
                    for (int side = 0; side < 2; side++)
                    {
                        if (edge.GetFace(side) is Face neighbour && neighbour.Id != face.Id)
                        {
                            generators.UnionWith(host.GetGeneratingElementIds(neighbour).Where(id => id != host.Id));
                            if (!across.TryGetValue(neighbour.Id, out List<string>? names))
                            {
                                across[neighbour.Id] = names = [];
                            }

                            names.Add(name);
                        }
                    }
                }

                (bool outer, double? enclosed, string? error) = Measure(face, edges);
                loops.Add(new LoopResult(index, outer, enclosed, [.. generators.Select(id => Name(document, id))], error));
                if (!outer)
                {
                    List<XYZ> points = [.. edges.Cast<Edge>().SelectMany(edge => edge.Tessellate())];
                    holeBoxes.Add((name, new XYZ(points.Min(p => p.X), points.Min(p => p.Y), 0), new XYZ(points.Max(p => p.X), points.Max(p => p.Y), 0)));
                }
            }
        }

        Dictionary<ElementId, List<Face>> generated = [];
        foreach (Face face in faces)
        {
            foreach (ElementId id in host.GetGeneratingElementIds(face).Where(id => id != host.Id))
            {
                if (!generated.TryGetValue(id, out List<Face>? known))
                {
                    generated[id] = known = [];
                }

                known.Add(face);
            }
        }

        // Everything geometric is read before the first deletion: a rolled-back
        // transaction regenerates the host and invalidates the faces read before it.
        List<(ElementId Id, List<FaceResult> Faces)> cutters = [.. generated.Select(entry => (entry.Key, entry.Value.Select(face => new FaceResult(
            Math.Round(Normal(face).Z, 4),
            M2(face.Area),
            face is PlanarFace { FaceNormal.Z: > UpFacing },
            across.TryGetValue(face.Id, out List<string>? names) ? [.. names.Distinct()] : [])).ToList()))];
        // Elements that could hide inside a hole, naming none of its faces:
        // joined or cutting elements, and every opening, whose plan box meets the hole's.
        List<string> hidden = [];
        IEnumerable<ElementId> suspects = JoinGeometryUtils.GetJoinedElements(document, host)
            .Concat(SolidSolidCutUtils.GetCuttingSolids(host))
            .Concat(new FilteredElementCollector(document).OfClass(typeof(Opening)).ToElementIds())
            .Distinct()
            .Where(id => !generated.ContainsKey(id));
        foreach (ElementId id in suspects)
        {
            if (document.GetElement(id) is not Element suspect || suspect.get_BoundingBox(null) is not BoundingBoxXYZ box)
            {
                continue;
            }

            foreach ((string name, XYZ min, XYZ max) in holeBoxes.Where(hole =>
                box.Min.X < hole.Max.X && box.Max.X > hole.Min.X && box.Min.Y < hole.Max.Y && box.Max.Y > hole.Min.Y))
            {
                hidden.Add($"{name} {suspect.GetType().Name} {suspect.Category?.BuiltInCategory} {suspect.UniqueId}");
            }
        }

        List<double?> sketchLoops = host is Floor floor && document.GetElement(floor.SketchId) is Sketch own
            ? [.. own.Profile.Cast<CurveArray>().Select(curves =>
            {
                try
                {
                    return (double?)M2(ExporterIFCUtils.ComputeAreaOfCurveLoops([CurveLoop.Create([.. curves.Cast<Curve>()])]));
                }
                catch (Exception)
                {
                    return null;
                }
            })]
            : [];

        double? area = Area(host);
        string typeName = document.GetElement(host.GetTypeId())?.Name ?? "?";
        double topArea = M2(top.Sum(face => face.Area));

        return new HostResult(
            host.Category?.BuiltInCategory.ToString() ?? "?",
            host.UniqueId,
            typeName,
            area is double feet ? M2(feet) : null,
            topArea,
            top.Count,
            loops,
            [.. cutters.Select(entry =>
            {
                Element? cutter = document.GetElement(entry.Id);
                string? owner = cutter is Sketch sketch ? $"{document.GetElement(sketch.OwnerId)?.GetType().Name} {Name(document, sketch.OwnerId)}" : null;
                string? hostOf = cutter switch
                {
                    FamilyInstance instance => instance.Host is { } h ? (h.Id == host.Id ? "this" : h.GetType().Name) : null,
                    Opening opening => opening.Host is { } h ? (h.Id == host.Id ? "this" : h.GetType().Name) : "none",
                    _ => null,
                };
                string name = Name(document, entry.Id), category = cutter?.Category?.BuiltInCategory.ToString() ?? "?", kind = cutter?.GetType().Name ?? "?";
                double? without = AreaWithout(document, host, entry.Id);
                double? delta = without is double after && area is double before ? M2(after - before) : null;
                double hole = loops.Where(loop => !loop.Outer && loop.Generators.Contains(name)).Sum(loop => loop.AreaM2 ?? 0);
                List<string> withoutJoins = [];
                if (delta is double given && hole - given > 1e-6)
                {
                    withoutJoins.AddRange(HolesWithout(document, host, entry.Id).Select(left => $"left after deleting it: {left}"));
                    // Only joins whose plan box meets one of this cutter's holes:
                    // undoing every join of a large slab regenerates the model
                    // hundreds of times.
                    List<(XYZ Min, XYZ Max)> its = [.. holeBoxes
                        .Where(box => loops.Any(loop => !loop.Outer && loop.Generators.Contains(name) && box.Name.EndsWith($".{loops.IndexOf(loop)}", StringComparison.Ordinal)))
                        .Select(box => (box.Min, box.Max))];
                    foreach (ElementId joined in JoinGeometryUtils.GetJoinedElements(document, host).Where(id =>
                        document.GetElement(id)?.get_BoundingBox(null) is BoundingBoxXYZ box
                        && its.Any(hole => box.Min.X < hole.Max.X && box.Max.X > hole.Min.X && box.Min.Y < hole.Max.Y && box.Max.Y > hole.Min.Y)))
                    {
                        double? unjoined = AreaWithout(document, host, joined, deleteToo: null);
                        double? both = AreaWithout(document, host, joined, deleteToo: entry.Id);
                        if (unjoined is double u && both is double b)
                        {
                            withoutJoins.Add($"{document.GetElement(joined)?.Category?.BuiltInCategory} {Name(document, joined)} {M2(b - u):F4}");
                        }
                    }
                }

                return new CutterResult(name, category, kind, owner, hostOf, entry.Faces, delta, withoutJoins);
            })],
            sketchLoops,
            hidden,
            [.. reading.Openings.Select(opening => $"{opening.UniqueId} {M2(opening.AreaSquareFeet):F4}")],
            [.. reading.Unmeasured.Select(opening => $"{opening.UniqueId}: {opening.Reason}")]);
    }

    /// <summary>The loop's orientation about the face's normal, and the area it encloses.</summary>
    private static (bool Outer, double? Area, string? Error) Measure(PlanarFace face, EdgeArray edges)
    {
        try
        {
            CurveLoop loop = CurveLoop.Create([.. edges.Cast<Edge>().Select(edge => edge.AsCurveFollowingFace(face))]);
            return (loop.IsCounterclockwise(face.FaceNormal), M2(ExporterIFCUtils.ComputeAreaOfCurveLoops([loop])), null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>The normal at the middle of the face's parameter range, which ComputeNormal requires the point to lie in.</summary>
    private static XYZ Normal(Face face)
    {
        BoundingBoxUV box = face.GetBoundingBox();
        return face is PlanarFace planar ? planar.FaceNormal : face.ComputeNormal(new UV((box.Min.U + box.Max.U) / 2, (box.Min.V + box.Max.V) / 2));
    }

    private static string Name(Document document, ElementId id) => document.GetElement(id)?.UniqueId ?? id.ToString();

    private static IEnumerable<Solid> Solids(Element element) =>
        element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })?.OfType<Solid>().Where(solid => solid.Faces.Size > 0) ?? [];

    private static double? Area(Element element) =>
        element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED) is { HasValue: true } parameter ? parameter.AsDouble() : null;

    /// <summary>Null when Revit refuses the deletion.</summary>
    private static double? AreaWithout(Document document, Element host, ElementId cutter)
    {
        using Transaction transaction = new(document, "Metrado probe (rolled back)");
        transaction.Start();
        try
        {
            document.Delete(cutter);
            document.Regenerate();
            return Area(host);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            transaction.RollBack();
        }
    }

    /// <summary>The holes left in the host's upper faces once the cutter is deleted, each with its area and generators.</summary>
    private static List<string> HolesWithout(Document document, HostObject host, ElementId cutter)
    {
        using Transaction transaction = new(document, "Metrado probe (rolled back)");
        transaction.Start();
        try
        {
            document.Delete(cutter);
            document.Regenerate();
            List<string> left = [];
            foreach (PlanarFace face in Solids(host).SelectMany(solid => solid.Faces.OfType<PlanarFace>()).Where(face => face.FaceNormal.Z > UpFacing))
            {
                foreach (EdgeArray edges in face.EdgeLoops.Cast<EdgeArray>())
                {
                    (bool outer, double? area, string? error) = Measure(face, edges);
                    HashSet<string> by = [.. edges.Cast<Edge>()
                        .SelectMany(edge => new[] { edge.GetFace(0), edge.GetFace(1) })
                        .OfType<Face>()
                        .Where(neighbour => neighbour.Id != face.Id)
                        .SelectMany(neighbour => host.GetGeneratingElementIds(neighbour))
                        .Select(id => id == host.Id ? "host" : $"{document.GetElement(id)?.GetType().Name} {Name(document, id)}")];
                    left.Add($"{(outer ? "outer" : "hole")} {area:F4} {error} by [{string.Join(", ", by)}]");
                }
            }

            return left;
        }
        catch (Exception ex)
        {
            return [$"failed: {ex.Message}"];
        }
        finally
        {
            transaction.RollBack();
        }
    }

    /// <summary>The host's area with one join undone and, optionally, one cutter deleted; null when Revit refuses.</summary>
    private static double? AreaWithout(Document document, Element host, ElementId joined, ElementId? deleteToo)
    {
        using Transaction transaction = new(document, "Metrado probe (rolled back)");
        transaction.Start();
        try
        {
            JoinGeometryUtils.UnjoinGeometry(document, host, document.GetElement(joined));
            if (deleteToo is ElementId cutter)
            {
                document.Delete(cutter);
            }

            document.Regenerate();
            return Area(host);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            transaction.RollBack();
        }
    }

    private static double M2(double squareFeet) => UnitUtils.ConvertFromInternalUnits(squareFeet, UnitTypeId.SquareMeters);
}
