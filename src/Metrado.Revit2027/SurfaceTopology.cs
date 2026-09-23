namespace Metrado.Revit2027;

/// <summary>One face of a floor or roof, by the id Revit gives it within the element's geometry.</summary>
/// <param name="Generators">The elements other than the host that generated it.</param>
/// <param name="UpperAreaSquareFeet">An up-facing face's area; null for any other face.</param>
public sealed record FaceFacts(int Id, IReadOnlyList<string> Generators, double? UpperAreaSquareFeet = null);

/// <summary>One edge loop of an up-facing face.</summary>
/// <param name="Outer">Counterclockwise about the face's normal: the face's outer boundary, not a hole.</param>
/// <param name="Across">The faces across its edges.</param>
/// <param name="Plan">Its points in plan, in order along the loop: an up-facing face projects onto plan without folding.</param>
public sealed record LoopFacts(int Face, bool Outer, double AreaSquareFeet, IReadOnlyList<int> Across, IReadOnlyList<(double X, double Y)> Plan);

/// <summary>
/// Turns the faces Revit reports for a floor or roof into the facts
/// <see cref="SurfaceOpeningPolicy"/> judges: which cuts line each hole in
/// the upper faces, and whether a cut names faces none of its holes explain.
/// Holds no Revit type.
/// </summary>
public static class SurfaceTopology
{
    /// <param name="kinds">What each element naming a face is; one missing is taken as joined.</param>
    /// <param name="openings">The opening elements the host hosts, which cut it whatever its faces say.</param>
    public static (IReadOnlyList<HoleFacts> Holes, IReadOnlyList<CutterFacts> Cutters) Assemble(
        IReadOnlyList<FaceFacts> faces,
        IReadOnlyList<LoopFacts> loops,
        IReadOnlyDictionary<string, CutterKind> kinds,
        IReadOnlyList<string> openings)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(openings);

        Dictionary<int, List<string>> generators = faces
            .GroupBy(face => face.Id)
            .ToDictionary(same => same.Key, same => same.SelectMany(face => face.Generators).Distinct().ToList());

        // A face's loops, outer less holes, must reproduce its area; where
        // they miss, some loop's area is not the face's.
        HashSet<int> addsUp = [.. loops
            .GroupBy(loop => loop.Face)
            .Where(face => faces.FirstOrDefault(known => known.Id == face.Key)?.UpperAreaSquareFeet is double area
                && Math.Abs(face.Sum(loop => loop.Outer ? loop.AreaSquareFeet : -loop.AreaSquareFeet) - area)
                    <= SurfaceOpeningPolicy.AreaTolerance)
            .Select(face => face.Key)];

        // A face across the hole that no other element generated is the
        // host's own outline, drawing the hole or part of it.
        List<(HoleFacts Hole, HashSet<int> Faces)> holes = [.. loops.Where(loop => !loop.Outer).Select(loop => (
            new HoleFacts(
                loop.AreaSquareFeet,
                [.. loop.Across.SelectMany(id => generators.GetValueOrDefault(id) ?? []).Distinct()],
                loop.Across.Any(id => generators.GetValueOrDefault(id) is not { Count: > 0 }),
                addsUp.Contains(loop.Face),
                HoldsIsland(loop, loops)),
            new HashSet<int>(loop.Across)))];

        List<string> cutting = [.. faces.SelectMany(face => face.Generators).Distinct()];
        List<CutterFacts> cutters = [.. cutting.Select(name =>
        {
            // Its holes' sides are the only faces a cut that stays within its
            // holes can name: an upper face it names is a seat of its own.
            HashSet<int> within = [.. holes.Where(hole => hole.Hole.Generators.Contains(name)).SelectMany(hole => hole.Faces)];
            return new CutterFacts(
                name,
                kinds.TryGetValue(name, out CutterKind kind) ? kind : CutterKind.Joined,
                faces.Any(face => face.Generators.Contains(name) && !within.Contains(face.Id)));
        })];

        cutters.AddRange(openings
            .Where(name => !cutting.Contains(name))
            .Select(name => new CutterFacts(name, CutterKind.Opening, BeyondItsHoles: false)));

        return ([.. holes.Select(hole => hole.Hole)], cutters);
    }

    /// <summary>
    /// Whether an outer loop of any up-facing face lies inside the hole in
    /// plan: an island of the host, which Revit did not remove. A loop Revit
    /// could not read may be an island's boundary, and a hole whose outline is
    /// not known cannot rule one out: both count.
    /// </summary>
    private static bool HoldsIsland(LoopFacts hole, IReadOnlyList<LoopFacts> loops) =>
        hole.Plan.Count < 3
        || loops.Any(other => !ReferenceEquals(other, hole)
            && (other.Outer || double.IsNaN(other.AreaSquareFeet))
            && other.Plan.Count > 0
            && Inside(hole.Plan, other.Plan[0]));

    /// <summary>Even-odd ray casting: whether the point lies inside the polygon.</summary>
    private static bool Inside(IReadOnlyList<(double X, double Y)> polygon, (double X, double Y) point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            (double X, double Y) a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < a.X + (point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
