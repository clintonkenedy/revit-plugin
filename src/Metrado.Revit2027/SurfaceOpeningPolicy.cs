namespace Metrado.Revit2027;

/// <summary>What an element cutting a floor or roof is, as far as its cut goes.</summary>
public enum CutterKind
{
    /// <summary>An opening element — a shaft, an opening by face or vertical — or its sketch.</summary>
    Opening = 1,

    /// <summary>A family instance this host hosts: a skylight, a floor-hosted opening.</summary>
    HostedInsert = 2,

    /// <summary>An unattached void instance cutting the host.</summary>
    VoidCut = 3,

    /// <summary>A joined element — a column, a beam, another floor — whose cut is not an opening.</summary>
    Joined = 4,
}

/// <summary>A hole in the host's upper faces: an inner edge loop of an up-facing face.</summary>
/// <param name="Generators">The elements other than the host generating the faces across its edges.</param>
/// <param name="DrawnByHost">Some face across its edges is the host's alone: its own outline draws the hole, or part of it.</param>
/// <param name="FaceAddsUp">The loops of the face holding it reproduce that face's area, so each loop's area is the face's.</param>
/// <param name="HoldsIsland">An outer loop of the host lies inside it: an island Revit did not remove.</param>
public sealed record HoleFacts(double AreaSquareFeet, IReadOnlyList<string> Generators, bool DrawnByHost, bool FaceAddsUp = true, bool HoldsIsland = false);

/// <param name="BeyondItsHoles">
/// It generates a face that lines none of its holes: it also cuts an edge,
/// leaves a face of its own facing up (a seat), or cuts the surface from
/// below. A cutter with no hole and nothing beyond them generates no face at
/// all: no face names it.
/// </param>
public sealed record CutterFacts(string UniqueId, CutterKind Kind, bool BeyondItsHoles);

/// <param name="ComputedSquareFeet">HOST_AREA_COMPUTED, when readable.</param>
/// <param name="UpperFacesSquareFeet">The up-facing planar faces' area.</param>
public sealed record SurfaceFacts(string UniqueId, double? ComputedSquareFeet, double UpperFacesSquareFeet, IReadOnlyList<HoleFacts> Holes);

/// <summary>
/// Decides which openings of a floor or roof are measured by the hole they
/// leave in its upper faces, and which are only reported.
///
/// A hole stands in for Revit's deduction only while the upper faces are what
/// Revit measured — they add up to its computed area — and the hole is one
/// opening's alone. On the Snowdon sample every such hole had exactly the
/// area Revit gave back when its opening was deleted. Anywhere else the
/// opening is reported and its deduction stands, as with walls.
/// </summary>
public static class SurfaceOpeningPolicy
{
    /// <summary>
    /// A millionth of a square foot, fixed: an area's error does not grow with
    /// the floor, so neither does the slack. The upper faces met the computed
    /// area to 7e-8 ft2 on both samples. A face's loops met its area less
    /// closely, up to 2e-4 ft2 on faces with no hole at all, where the face's
    /// own area strays from loops that match the sketch; a hole on such a face
    /// is reported, erring toward the deduction.
    /// </summary>
    internal const double AreaTolerance = 1e-6;

    private const string HoldsAnIsland =
        "an island of the host lies inside its hole, and Revit did not remove the island";

    private const string FaceMissesItsArea =
        "the edge loops of the face holding its hole do not add up to that face's area, so the hole's own area cannot be trusted";

    public static OpeningDecision Decide(SurfaceFacts surface, IReadOnlyList<CutterFacts> cutters)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(cutters);

        string? everyHole = surface.ComputedSquareFeet is double computed
            && Math.Abs(surface.UpperFacesSquareFeet - computed) <= AreaTolerance
            ? null
            : "its upper faces do not add up to its computed area, so no hole in them can be trusted to equal Revit's deduction";

        // An opening no face names cut the host somewhere, and its hole would
        // look like one the host's own outline draws.
        bool unnamed = cutters.Any(cutter => cutter.Kind != CutterKind.Joined && !cutter.BeyondItsHoles
            && !surface.Holes.Any(hole => hole.Generators.Contains(cutter.UniqueId)));

        List<(string UniqueId, double Area, string? Reason)> openings = [];

        foreach (CutterFacts cutter in cutters.Where(cutter => cutter.Kind != CutterKind.Joined))
        {
            List<HoleFacts> holes = [.. surface.Holes.Where(hole => hole.Generators.Contains(cutter.UniqueId))];
            double area = holes.Sum(hole => hole.AreaSquareFeet);
            openings.Add((cutter.UniqueId, area, everyHole ?? cutter switch
            {
                { BeyondItsHoles: false } when holes.Count == 0 => "no face of the host names it, so its cut cannot be located",
                _ when holes.Count == 0 => "no hole in the upper faces is its own: it cuts an edge, or the surface only from below",
                _ when holes.Any(Shared) => "its hole is shared with another cut, and Revit deducts their union only once",
                _ when holes.Any(hole => !hole.FaceAddsUp) => FaceMissesItsArea,
                _ when holes.Any(hole => hole.HoldsIsland) => HoldsAnIsland,
                { BeyondItsHoles: true } => "it names faces beyond the sides of its holes, such as an edge it notches or a seat it leaves facing up, which no hole outlines",
                _ => Measurable(area),
            }));
        }

        List<HoleFacts> own = [.. surface.Holes.Where(hole => hole.DrawnByHost)];
        for (int index = 0; index < own.Count; index++)
        {
            HoleFacts hole = own[index];
            openings.Add(($"{surface.UniqueId}/hole-{index + 1}", hole.AreaSquareFeet, everyHole switch
            {
                string reason => reason,
                _ when Shared(hole) => "this hole in its own outline is shared with another cut, and Revit deducts their union only once",
                _ when unnamed => "an opening no face of the host names cuts it too, so this hole may be that opening's",
                _ when !hole.FaceAddsUp => FaceMissesItsArea,
                _ when hole.HoldsIsland => HoldsAnIsland,
                _ => Measurable(hole.AreaSquareFeet),
            }));
        }

        return new OpeningDecision(
            [.. openings.Where(opening => opening.Reason is null).Select(opening => new OpeningReading(opening.UniqueId, opening.Area))],
            [.. openings.Where(opening => opening.Reason is not null).Select(opening => new UnmeasuredOpening(opening.UniqueId, opening.Reason!))]);
    }

    /// <summary>More than one cut bounds the hole: the host's own outline counts as one.</summary>
    private static bool Shared(HoleFacts hole) => hole.Generators.Count + (hole.DrawnByHost ? 1 : 0) > 1;

    private static string? Measurable(double area) =>
        area > 0 && double.IsFinite(area) ? null : "its hole has no area";
}
