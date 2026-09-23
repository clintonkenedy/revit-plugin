namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins which openings a wall's reading measures and which it only reports.
///
/// No read-only Revit API returns the exact area Revit subtracted for one
/// insert, so every measured area is an outline standing in for it. The rule
/// the policy keeps is one-directional: when the outline cannot be trusted,
/// the opening is reported and its deduction stands. It is never given a
/// number that could add back material Revit did not remove.
/// </summary>
public sealed class OpeningPolicyTests
{
    private static readonly Box Wall = new(UMin: 0, UMax: 20, ZMin: 0, ZMax: 10);

    [Fact]
    public void ACleanHostedCutIsMeasuredByItsOutline()
    {
        OpeningDecision decision = Decide(Door());

        Assert.Equal([new OpeningReading("door", 21.0)], decision.Measured);
        Assert.Empty(decision.Unmeasured);
    }

    /// <summary>
    /// Hosted inserts are listed for every wall a joined wall shares them with,
    /// but only the walls whose faces they generate were cut. On the Snowdon
    /// sample every one of those non-cutting inserts had a true deduction of 0,
    /// while the outline would have claimed up to 2.7 m2.
    /// </summary>
    [Fact]
    public void AnInsertThatCutsNothingIsNotAnOpeningAtAll()
    {
        OpeningDecision decision = Decide(Door() with { CutsWall = false });

        Assert.Empty(decision.Measured);
        Assert.Empty(decision.Unmeasured);
    }

    public static TheoryData<string, InsertFacts, string> Untrustworthy() => new()
    {
        { "shadow cut", Door() with { HostedByWall = false }, "hosted by another" },
        { "void cut", Door() with { IsVoidCut = true }, "void" },
        { "embedded wall", Door() with { Kind = InsertKind.EmbeddedWall, CutoutSquareFeet = null }, "embedded" },
        { "unknown kind", Door() with { Kind = InsertKind.Other }, "cannot be measured" },
        { "no outline", Door() with { CutoutSquareFeet = null, CutoutError = "Couldn't generate cut-out." }, "Couldn't generate cut-out." },
        { "zero outline", Door() with { CutoutSquareFeet = 0.0 }, "no area" },
        { "non-finite outline", Door() with { CutoutSquareFeet = double.NaN }, "no area" },
        { "beyond the top", Door() with { Outline = new Box(1, 4, 0, 10.5) }, "beyond the wall" },
        { "beyond the end", Door() with { Outline = new Box(-0.5, 3, 0, 7) }, "beyond the wall" },
    };

    [Theory]
    [MemberData(nameof(Untrustworthy))]
    public void AnUntrustworthyOutlineIsReportedWithItsReasonNeverMeasured(string _, InsertFacts insert, string reason)
    {
        OpeningDecision decision = Decide(insert);

        Assert.Empty(decision.Measured);
        UnmeasuredOpening unmeasured = Assert.Single(decision.Unmeasured);
        Assert.Equal("door", unmeasured.UniqueId);
        Assert.Contains(reason, unmeasured.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Revit deducts two overlapping openings' union once; their two outlines
    /// would count the shared part twice. Neither can be trusted alone.
    /// </summary>
    [Fact]
    public void OverlappingOutlinesAreBothReported()
    {
        OpeningDecision decision = Decide(
            Door() with { UniqueId = "a", Outline = new Box(1, 4, 1, 3) },
            Door() with { UniqueId = "b", Outline = new Box(3, 6, 2, 4) });

        Assert.Empty(decision.Measured);
        Assert.Equal(["a", "b"], decision.Unmeasured.Select(opening => opening.UniqueId));
        Assert.All(decision.Unmeasured, opening => Assert.Contains("overlaps", opening.Reason));
    }

    /// <summary>
    /// The overlap that mattered on the Snowdon sample: three doors inside an
    /// embedded curtain wall. The embedded wall cannot be measured, yet it
    /// still removes the doors' area — deleting one door restored 0.30 m2
    /// while its outline claimed 5.49 m2. Any cutting insert's outline, even
    /// an unmeasurable one's, rules an overlapping opening out.
    /// </summary>
    [Fact]
    public void AnOpeningOverlappingAnUnmeasurableCutIsReported()
    {
        OpeningDecision decision = Decide(
            Door() with { UniqueId = "door", Outline = new Box(2, 4, 0, 7) },
            Door() with { UniqueId = "embedded", Kind = InsertKind.EmbeddedWall, CutoutSquareFeet = null, Outline = new Box(1, 9, 0, 9) });

        Assert.Empty(decision.Measured);
        Assert.Contains(decision.Unmeasured, opening => opening.UniqueId == "door" && opening.Reason.Contains("overlaps"));
    }

    /// <summary>
    /// When a cutting insert cannot even be located, no overlap with it can be
    /// ruled out, so no opening of that wall is measured.
    /// </summary>
    [Fact]
    public void AnUnlocatableCutReportsEveryOpeningOfTheWall()
    {
        OpeningDecision decision = Decide(
            Door() with { UniqueId = "door" },
            Door() with { UniqueId = "somewhere", Kind = InsertKind.Other, CutoutSquareFeet = null, Outline = null });

        Assert.Empty(decision.Measured);
        Assert.Contains(decision.Unmeasured, opening => opening.UniqueId == "door" && opening.Reason.Contains("cannot be located"));
    }

    /// <summary>An insert that cuts nothing cannot overlap anything that matters.</summary>
    [Fact]
    public void ANonCuttingInsertNeverRulesAnOpeningOut()
    {
        OpeningDecision decision = Decide(
            Door() with { UniqueId = "door", Outline = new Box(2, 4, 0, 7) },
            Door() with { UniqueId = "elsewhere", CutsWall = false, Outline = new Box(1, 9, 0, 9) },
            Door() with { UniqueId = "unlocated", CutsWall = false, Outline = null });

        Assert.Equal(["door"], decision.Measured.Select(opening => opening.UniqueId));
    }

    [Fact]
    public void OutlinesThatOnlyTouchDoNotOverlap()
    {
        OpeningDecision decision = Decide(
            Door() with { UniqueId = "a", Outline = new Box(1, 4, 1, 3) },
            Door() with { UniqueId = "b", Outline = new Box(4, 6, 1, 3) });

        Assert.Equal(["a", "b"], decision.Measured.Select(opening => opening.UniqueId));
    }

    /// <summary>
    /// A wall condition that makes Revit's deduction differ from the outline
    /// — sweeps or reveals, an edited profile, an attached top — puts every
    /// opening of that wall on the reported side, named with the condition.
    /// </summary>
    [Fact]
    public void AWallConditionReportsEveryOpeningOfThatWall()
    {
        OpeningDecision decision = OpeningPolicy.Decide(
            new WallFacts(Wall, ["its type carries sweeps or reveals"]),
            [Door() with { UniqueId = "a", Outline = new Box(1, 3, 0, 7) }, Door() with { UniqueId = "b", Outline = new Box(5, 7, 0, 7) }]);

        Assert.Empty(decision.Measured);
        Assert.All(decision.Unmeasured, opening => Assert.Contains("sweeps or reveals", opening.Reason));
        Assert.Equal(2, decision.Unmeasured.Count);
    }

    [Fact]
    public void AnOutlineExactlyOnTheWallEdgeIsInside()
    {
        OpeningDecision decision = Decide(Door() with { Outline = new Box(0, 3, 0, 7) });

        Assert.Single(decision.Measured);
    }

    private static OpeningDecision Decide(params InsertFacts[] inserts) =>
        OpeningPolicy.Decide(new WallFacts(Wall, []), inserts);

    private static InsertFacts Door() => new(
        UniqueId: "door",
        Kind: InsertKind.FamilyInstance,
        CutsWall: true,
        HostedByWall: true,
        IsVoidCut: false,
        CutoutSquareFeet: 21.0,
        CutoutError: null,
        Outline: new Box(1, 4, 0, 7));
}
