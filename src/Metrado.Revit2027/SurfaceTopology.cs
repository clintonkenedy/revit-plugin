namespace Metrado.Revit2027;

/// <summary>One face of a floor or roof, by the id Revit gives it within the element's geometry.</summary>
/// <param name="Generators">The elements other than the host that generated it.</param>
public sealed record FaceFacts(int Id, IReadOnlyList<string> Generators);

/// <summary>One edge loop of an up-facing face.</summary>
/// <param name="Outer">Counterclockwise about the face's normal: the face's outer boundary, not a hole.</param>
/// <param name="Across">The faces across its edges.</param>
public sealed record LoopFacts(int Face, bool Outer, double AreaSquareFeet, IReadOnlyList<int> Across);

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

        // A face across the hole that no other element generated is the
        // host's own outline, drawing the hole or part of it.
        List<(HoleFacts Hole, HashSet<int> Faces)> holes = [.. loops.Where(loop => !loop.Outer).Select(loop => (
            new HoleFacts(
                loop.AreaSquareFeet,
                [.. loop.Across.SelectMany(id => generators.GetValueOrDefault(id) ?? []).Distinct()],
                loop.Across.Any(id => generators.GetValueOrDefault(id) is not { Count: > 0 })),
            new HashSet<int>([loop.Face, .. loop.Across])))];

        List<string> cutting = [.. faces.SelectMany(face => face.Generators).Distinct()];
        List<CutterFacts> cutters = [.. cutting.Select(name =>
        {
            // Its holes' faces, and the upper faces holding them, are the only
            // faces a cut that stays within its holes can name.
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
}
