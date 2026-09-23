namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins how the faces Revit reports for a floor or roof become the facts
/// <see cref="SurfaceOpeningPolicy"/> judges: which cut each hole in the upper
/// faces belongs to, and whether a cut reaches beyond its holes.
/// </summary>
public sealed class SurfaceTopologyTests
{
    /// <summary>Face 1 is the upper face; 2 to 5 line a shaft's hole; 6 is the slab's own edge.</summary>
    private static readonly FaceFacts[] Faces =
    [
        new(1, []),
        new(2, ["shaft"]), new(3, ["shaft"]), new(4, ["shaft"]), new(5, ["shaft"]),
        new(6, []),
    ];

    private static readonly LoopFacts Outer = new(Face: 1, Outer: true, AreaSquareFeet: 1000.0, Across: [6], Plan: Square(-20, -20, 40));
    private static readonly LoopFacts ShaftHole = new(Face: 1, Outer: false, AreaSquareFeet: 4.8, Across: [2, 3, 4, 5], Plan: Square(0, 0, 4));

    [Fact]
    public void AHoleBelongsToTheCutsAcrossItsEdges()
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble(Faces, [Outer, ShaftHole]);

        HoleFacts hole = Assert.Single(holes);
        Assert.Equal(4.8, hole.AreaSquareFeet);
        Assert.Equal(["shaft"], hole.Generators);
        Assert.False(hole.DrawnByHost);
    }

    /// <summary>The outer boundary is the face itself, never a hole.</summary>
    [Fact]
    public void TheOuterLoopIsNoHole()
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble(Faces, [Outer]);

        Assert.Empty(holes);
    }

    /// <summary>
    /// A face's loops must reproduce its area: outer less holes. A loop's
    /// error does not grow with the face, so neither does the slack: where
    /// they miss by more than a millionth of a square foot, a loop's area is
    /// not the face's.
    /// </summary>
    [Theory]
    [InlineData(995.2, true)]
    [InlineData(995.2000005, true)]
    [InlineData(995.200002, false)]
    [InlineData(995.199998, false)]
    [InlineData(null, false)]
    public void AHoleIsTrustedOnlyWhereItsFacesLoopsAddUp(double? faceArea, bool addsUp)
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble([new(1, [], faceArea), .. Faces.Skip(1)], [Outer, ShaftHole]);

        Assert.Equal(addsUp, Assert.Single(holes).FaceAddsUp);
    }

    /// <summary>Each face is judged on its own loops, not on another face's.</summary>
    [Fact]
    public void EachFaceIsJudgedOnItsOwnLoops()
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble(
            [new(1, [], 995.2), .. Faces.Skip(1), new(11, [], 49.0)],
            [Outer, ShaftHole, new LoopFacts(11, true, 50.0, [], Square(100, 100, 7)), new LoopFacts(11, false, 1.0, [], Square(101, 101, 1))]);

        Assert.Equal([true, true], holes.Select(hole => hole.FaceAddsUp));
    }

    /// <summary>A face across the hole that no other element generated is the host's own outline.</summary>
    [Fact]
    public void AFaceOnlyTheHostGeneratedMakesTheHoleItsOwn()
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble(Faces, [ShaftHole with { Across = [2, 3, 6] }]);

        Assert.True(Assert.Single(holes).DrawnByHost);
    }

    /// <summary>A face named twice, or by two cuts, lists each cut once.</summary>
    [Fact]
    public void EachCutIsListedOncePerHole()
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble([.. Faces, new(7, ["shaft", "column"])], [ShaftHole with { Across = [2, 3, 7] }]);

        Assert.Equal(["shaft", "column"], Assert.Single(holes).Generators);
    }

    [Fact]
    public void ACutThatOnlyLinesItsHolesStaysWithinThem()
    {
        (_, IReadOnlyList<CutterFacts> cutters) = Assemble(Faces, [Outer, ShaftHole]);

        Assert.Equal([new CutterFacts("shaft", CutterKind.Opening, BeyondItsHoles: false)], cutters);
    }

    /// <summary>
    /// A cut that names an upper face leaves one of its own facing up, a seat
    /// over a through-hole, which Revit counts as area: its holes would sum
    /// more than it removed. No opening named one on either sample.
    /// </summary>
    [Fact]
    public void AnUpperFaceTheCutNamesIsBeyondItsHoles()
    {
        (_, IReadOnlyList<CutterFacts> cutters) = Assemble([new(1, ["shaft"]), .. Faces.Skip(1)], [Outer, ShaftHole]);

        Assert.True(Assert.Single(cutters).BeyondItsHoles);
    }

    /// <summary>An outer loop inside a hole is an island of the host, which Revit did not remove.</summary>
    [Fact]
    public void AHoleWithAnOuterLoopInsideItHoldsAnIsland()
    {
        LoopFacts island = new(11, true, 1.0, [], Square(1, 1, 1));

        (IReadOnlyList<HoleFacts> holes, _) = Assemble([.. Faces, new(11, [], 1.0)], [Outer, ShaftHole, island]);

        Assert.True(Assert.Single(holes).HoldsIsland);
    }

    /// <summary>A hole whose outline could not be read cannot rule an island out.</summary>
    [Fact]
    public void AHoleOfUnknownOutlineIsTakenToHoldAnIsland()
    {
        (IReadOnlyList<HoleFacts> holes, _) = Assemble(Faces, [Outer, ShaftHole with { Plan = [(0, 0), (4, 0)] }]);

        Assert.True(Assert.Single(holes).HoldsIsland);
    }

    /// <summary>The face's own boundary surrounds the hole, and an outer loop elsewhere is beside it: neither is an island.</summary>
    [Fact]
    public void AnOuterLoopAroundOrBesideTheHoleIsNoIsland()
    {
        LoopFacts beside = new(11, true, 1.0, [], Square(-10, 1, 1));

        (IReadOnlyList<HoleFacts> holes, _) = Assemble([.. Faces, new(11, [], 1.0)], [Outer, ShaftHole, beside]);

        Assert.False(Assert.Single(holes).HoldsIsland);
    }

    /// <summary>A notch at the edge, a face underneath: faces no hole of its own explains.</summary>
    [Fact]
    public void ACutNamingAFaceNoHoleOfItsOwnExplainsReachesBeyond()
    {
        (_, IReadOnlyList<CutterFacts> cutters) = Assemble([.. Faces, new(8, ["shaft"])], [Outer, ShaftHole]);

        Assert.True(Assert.Single(cutters).BeyondItsHoles);
    }

    /// <summary>Another cut's hole does not explain this cut's faces.</summary>
    [Fact]
    public void AnotherCutsHoleDoesNotExplainTheseFaces()
    {
        (_, IReadOnlyList<CutterFacts> cutters) = Assemble(
            [.. Faces, new(9, ["sleeve"]), new(10, ["shaft"])],
            [Outer, ShaftHole with { Across = [2, 3, 4, 5] }, ShaftHole with { Across = [9, 10] }],
            kinds: new Dictionary<string, CutterKind> { ["shaft"] = CutterKind.Opening, ["sleeve"] = CutterKind.VoidCut });

        Assert.Equal([false, false], cutters.Select(cutter => cutter.BeyondItsHoles));

        (_, cutters) = Assemble(
            [.. Faces, new(9, ["sleeve"]), new(10, ["sleeve"])],
            [Outer, ShaftHole, ShaftHole with { Across = [9] }],
            kinds: new Dictionary<string, CutterKind> { ["shaft"] = CutterKind.Opening, ["sleeve"] = CutterKind.VoidCut });

        Assert.Equal([false, true], cutters.Select(cutter => cutter.BeyondItsHoles));

        // The upper face holding the shaft's hole explains the shaft naming
        // it, not a sleeve that notches that face with no hole of its own.
        (_, cutters) = Assemble(
            [new(1, ["sleeve"]), .. Faces.Skip(1)],
            [Outer, ShaftHole],
            kinds: new Dictionary<string, CutterKind> { ["shaft"] = CutterKind.Opening, ["sleeve"] = CutterKind.VoidCut });

        Assert.Equal([("sleeve", true), ("shaft", false)], cutters.Select(cutter => (cutter.UniqueId, cutter.BeyondItsHoles)));
    }

    /// <summary>Cuts come in the order the faces first name them, each with its own kind.</summary>
    [Fact]
    public void CutsKeepTheFacesOrderAndTheirKinds()
    {
        (_, IReadOnlyList<CutterFacts> cutters) = Assemble(
            [new(1, ["column"]), new(2, ["shaft", "column"]), new(3, ["planter", "unknown"])],
            [],
            kinds: new Dictionary<string, CutterKind> { ["shaft"] = CutterKind.Opening, ["column"] = CutterKind.Joined, ["planter"] = CutterKind.HostedInsert });

        Assert.Equal(
            [("column", CutterKind.Joined), ("shaft", CutterKind.Opening), ("planter", CutterKind.HostedInsert), ("unknown", CutterKind.Joined)],
            cutters.Select(cutter => (cutter.UniqueId, cutter.Kind)));
    }

    /// <summary>
    /// An opening element the host hosts is a cut whatever its faces say: one
    /// no face names joins the cuts, with nothing beyond its (absent) holes.
    /// </summary>
    [Fact]
    public void AnOpeningNoFaceNamesJoinsTheCutsOnce()
    {
        (_, IReadOnlyList<CutterFacts> cutters) = Assemble(Faces, [Outer, ShaftHole], openings: ["shaft", "hidden"]);

        Assert.Equal(
            [new CutterFacts("shaft", CutterKind.Opening, false), new CutterFacts("hidden", CutterKind.Opening, false)],
            cutters);
    }

    /// <summary>A square in plan, corner first, counterclockwise.</summary>
    private static (double X, double Y)[] Square(double x, double y, double side) =>
        [(x, y), (x + side, y), (x + side, y + side), (x, y + side)];

    private static (IReadOnlyList<HoleFacts> Holes, IReadOnlyList<CutterFacts> Cutters) Assemble(
        IReadOnlyList<FaceFacts> faces,
        IReadOnlyList<LoopFacts> loops,
        IReadOnlyDictionary<string, CutterKind>? kinds = null,
        IReadOnlyList<string>? openings = null) =>
        SurfaceTopology.Assemble(faces, loops, kinds ?? new Dictionary<string, CutterKind> { ["shaft"] = CutterKind.Opening }, openings ?? []);
}
