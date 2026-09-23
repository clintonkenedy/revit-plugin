namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins which openings of a floor or roof are measured by the hole they leave
/// in its upper faces, and which are only reported (task 2.6's second half).
///
/// A hole's area stands in for Revit's deduction only while the upper faces
/// are what Revit measured and the hole is one opening's alone. Everywhere
/// else the opening is reported and its deduction stands, as with walls: the
/// error may understate a metrado, never invent material.
/// </summary>
public sealed class SurfaceOpeningPolicyTests
{
    [Fact]
    public void AnOpeningThatAloneBoundsAHoleIsMeasuredByIt()
    {
        OpeningDecision decision = Decide(Slab(Hole(4.8, "shaft")), Cutter("shaft"));

        Assert.Equal([new OpeningReading("shaft", 4.8)], decision.Measured);
        Assert.Empty(decision.Unmeasured);
    }

    /// <summary>A skylight in a roof, a sleeve's void: every opening kind is measured the same way.</summary>
    [Theory]
    [InlineData(CutterKind.Opening)]
    [InlineData(CutterKind.HostedInsert)]
    [InlineData(CutterKind.VoidCut)]
    public void EveryOpeningKindIsMeasuredByItsHole(CutterKind kind)
    {
        OpeningDecision decision = Decide(Slab(Hole(2.5, "cut")), Cutter("cut", kind));

        Assert.Equal([new OpeningReading("cut", 2.5)], decision.Measured);
    }

    /// <summary>A hole drawn in the floor's own boundary has no element, so it is named after the floor.</summary>
    [Fact]
    public void AHoleInTheHostsOwnOutlineIsMeasuredUnderTheHostsName()
    {
        OpeningDecision decision = Decide(Slab(Hole(3.4), Hole(0.5)));

        Assert.Equal([new OpeningReading("floor/hole-1", 3.4), new OpeningReading("floor/hole-2", 0.5)], decision.Measured);
    }

    /// <summary>One opening, one quantity: a shaft whose sketch has two loops is one opening.</summary>
    [Fact]
    public void AnOpeningWithSeveralHolesIsMeasuredByTheirSum()
    {
        OpeningDecision decision = Decide(Slab(Hole(4.75, "shaft"), Hole(1.25, "shaft")), Cutter("shaft"));

        Assert.Equal([new OpeningReading("shaft", 6.0)], decision.Measured);
    }

    /// <summary>
    /// A column through a slab, a floor joined to it: the material is the other
    /// element's, so its deduction stands and nothing is said, as with walls.
    /// </summary>
    [Fact]
    public void AJoinedElementIsNeitherMeasuredNorReported()
    {
        OpeningDecision decision = Decide(Slab(Hole(2.0, "column")), Cutter("column", CutterKind.Joined));

        Assert.Empty(decision.Measured);
        Assert.Empty(decision.Unmeasured);
    }

    public static TheoryData<string, SurfaceFacts, string> Untrustworthy() => new()
    {
        // On the Snowdon sample, shafts across a floor's edge removed up to 7.7 m2 and left no hole.
        { "an edge notch", Slab(), "no hole in the upper faces is its own" },
        { "a cut beyond its hole", Slab(Hole(4.8, "shaft")), "beyond the sides of its holes" },
        // An island of the floor inside the hole is floor Revit did not remove.
        { "a hole holding an island", Slab(Hole(4.8, "shaft") with { HoldsIsland = true }), "island" },
        { "a hole shared with a joined element", Slab(Hole(4.8, "shaft", "column")), "shared" },
        { "a hole with no area", Slab(Hole(0.0, "shaft")), "no area" },
        { "a hole of unknown area", Slab(Hole(double.NaN, "shaft")), "no area" },
        { "a hole of infinite area", Slab(Hole(double.PositiveInfinity, "shaft")), "no area" },
        // On the Snowdon sample one shaft's loop measured 0.0018 m2 more than
        // it deducted, on exactly the five faces whose loops missed their area.
        { "a face whose loops miss its area", Slab(Hole(4.8, "shaft") with { FaceAddsUp = false }), "do not add up to that face" },
        { "faces that do not add up", Slab(Hole(4.8, "shaft")) with { UpperFacesSquareFeet = 1001.0 }, "do not add up" },
        { "no computed area", Slab(Hole(4.8, "shaft")) with { ComputedSquareFeet = null }, "do not add up" },
    };

    [Theory]
    [MemberData(nameof(Untrustworthy))]
    public void AnOpeningWhoseHoleCannotBeTrustedIsOnlyReported(string condition, SurfaceFacts slab, string reason)
    {
        CutterFacts shaft = Cutter("shaft") with { BeyondItsHoles = condition is "a cut beyond its hole" or "an edge notch" };

        OpeningDecision decision = Decide(slab, shaft, Cutter("column", CutterKind.Joined));

        Assert.Empty(decision.Measured);
        UnmeasuredOpening opening = Assert.Single(decision.Unmeasured);
        Assert.Equal("shaft", opening.UniqueId);
        Assert.Contains(reason, opening.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// An opening element that no face names still cut the host — on the
    /// Snowdon sample one removed 11.4 m2 from a wall so — and its hole would
    /// look like one the host's own outline draws. Neither can be measured.
    /// </summary>
    [Fact]
    public void AnOpeningNoFaceNamesIsReportedAndSpoilsTheOwnHoles()
    {
        OpeningDecision decision = Decide(Slab(Hole(0.5)), Cutter("opening"));

        Assert.Empty(decision.Measured);
        Assert.Collection(
            decision.Unmeasured,
            opening => Assert.Equal(("opening", true), (opening.UniqueId, opening.Reason.Contains("names", StringComparison.Ordinal))),
            hole => Assert.Equal(("floor/hole-1", true), (hole.UniqueId, hole.Reason.Contains("names", StringComparison.Ordinal))));
    }

    /// <summary>A notch is located, at the edge, and a joined element is no opening: neither hides one in the host's own holes.</summary>
    [Fact]
    public void ALocatedCutLeavesTheOwnHolesMeasured()
    {
        OpeningDecision decision = Decide(
            Slab(Hole(0.5)),
            Cutter("shaft") with { BeyondItsHoles = true },
            Cutter("column", CutterKind.Joined));

        Assert.Equal([new OpeningReading("floor/hole-1", 0.5)], decision.Measured);
        Assert.Equal("shaft", Assert.Single(decision.Unmeasured).UniqueId);
    }

    /// <summary>One untrusted hole spoils the opening, wherever it sits among its holes.</summary>
    [Fact]
    public void OneHoleOnAFaceThatMissesItsAreaSpoilsTheOpening()
    {
        OpeningDecision decision = Decide(Slab(Hole(4.8, "shaft"), Hole(1.0, "shaft") with { FaceAddsUp = false }), Cutter("shaft"));

        Assert.Empty(decision.Measured);
        Assert.Contains("do not add up to that face", Assert.Single(decision.Unmeasured).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACutBeyondSeveralHolesIsReported()
    {
        OpeningDecision decision = Decide(Slab(Hole(0.4, "shaft"), Hole(0.4, "shaft")), Cutter("shaft") with { BeyondItsHoles = true });

        Assert.Empty(decision.Measured);
        Assert.Contains("beyond the sides of its holes", Assert.Single(decision.Unmeasured).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void AnOwnHoleWithNoAreaIsReported(double area)
    {
        OpeningDecision decision = Decide(Slab(Hole(area)));

        Assert.Empty(decision.Measured);
        Assert.Contains("no area", Assert.Single(decision.Unmeasured).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnHoleHoldingAnIslandIsReported()
    {
        OpeningDecision decision = Decide(Slab(Hole(0.5) with { HoldsIsland = true }));

        Assert.Empty(decision.Measured);
        Assert.Contains("island", Assert.Single(decision.Unmeasured).Reason, StringComparison.Ordinal);
    }

    /// <summary>Two openings whose cuts merge into one hole: Revit deducts their union once.</summary>
    [Fact]
    public void OpeningsSharingAHoleAreBothReported()
    {
        OpeningDecision decision = Decide(Slab(Hole(6.0, "shaft", "sleeve")), Cutter("shaft"), Cutter("sleeve", CutterKind.VoidCut));

        Assert.Empty(decision.Measured);
        Assert.Equal(["shaft", "sleeve"], decision.Unmeasured.Select(opening => opening.UniqueId));
    }

    /// <summary>One shared hole spoils the opening's other, clean one: its quantity would be only part of its cut.</summary>
    [Fact]
    public void AnOpeningWithOneSharedHoleIsReportedWhole()
    {
        OpeningDecision decision = Decide(Slab(Hole(4.8, "shaft"), Hole(1.0, "shaft", "column")), Cutter("shaft"), Cutter("column", CutterKind.Joined));

        Assert.Empty(decision.Measured);
        Assert.Equal("shaft", Assert.Single(decision.Unmeasured).UniqueId);
    }

    /// <summary>A shaft over a hole the floor's outline already draws in part: both are reported.</summary>
    [Fact]
    public void AHolePartlyTheHostsOwnOutlineReportsBoth()
    {
        OpeningDecision decision = Decide(Slab(Hole(4.8, "shaft") with { DrawnByHost = true }), Cutter("shaft"));

        Assert.Empty(decision.Measured);
        Assert.Equal(["shaft", "floor/hole-1"], decision.Unmeasured.Select(opening => opening.UniqueId));
        Assert.All(decision.Unmeasured, opening => Assert.Contains("shared", opening.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void AnOwnHoleOnAFaceWhoseLoopsMissItsAreaIsReported()
    {
        OpeningDecision decision = Decide(Slab(Hole(0.5) with { FaceAddsUp = false }));

        Assert.Empty(decision.Measured);
        Assert.Contains("do not add up to that face", Assert.Single(decision.Unmeasured).Reason, StringComparison.Ordinal);
    }

    /// <summary>The host's own hole with a column in it: neither part can be measured alone.</summary>
    [Fact]
    public void AnOwnHoleSharedWithAnotherCutIsReported()
    {
        OpeningDecision decision = Decide(Slab(Hole(3.0, "column") with { DrawnByHost = true }), Cutter("column", CutterKind.Joined));

        Assert.Empty(decision.Measured);
        UnmeasuredOpening opening = Assert.Single(decision.Unmeasured);
        Assert.Equal("floor/hole-1", opening.UniqueId);
        Assert.Contains("shared", opening.Reason, StringComparison.Ordinal);
    }

    /// <summary>When the upper faces are not what Revit measured, no hole in them is trusted, the host's own included.</summary>
    [Fact]
    public void FacesThatDoNotAddUpLeaveEveryOpeningReported()
    {
        OpeningDecision decision = Decide(
            Slab(Hole(4.8, "shaft"), Hole(0.5)) with { UpperFacesSquareFeet = 999.0 },
            Cutter("shaft"));

        Assert.Empty(decision.Measured);
        Assert.Equal(["shaft", "floor/hole-1"], decision.Unmeasured.Select(opening => opening.UniqueId));
    }

    /// <summary>
    /// Faces and parameter agree to floating-point noise only, whatever the
    /// floor's size: on both samples, 7e-8 ft2 at most. Half a millionth of a
    /// square foot is noise; two millionths is not.
    /// </summary>
    [Theory]
    [InlineData(1000.0000005, true)]
    [InlineData(999.9999995, true)]
    [InlineData(1000.000002, false)]
    [InlineData(999.999998, false)]
    public void TheFacesMustMatchTheComputedAreaToNoiseOnly(double upperFaces, bool measured)
    {
        OpeningDecision decision = Decide(Slab(Hole(4.8, "shaft")) with { UpperFacesSquareFeet = upperFaces }, Cutter("shaft"));

        Assert.Equal(measured, decision.Measured.Count == 1);
    }

    /// <summary>Openings keep the order the cutters came in, the host's own holes last.</summary>
    [Fact]
    public void OpeningsKeepTheCuttersOrder()
    {
        OpeningDecision decision = Decide(
            Slab(Hole(1.0), Hole(2.0, "b"), Hole(3.0, "a"), Hole(4.0, "a", "c")),
            Cutter("c"), Cutter("b"), Cutter("a"));

        Assert.Equal(["b", "floor/hole-1"], decision.Measured.Select(opening => opening.UniqueId));
        Assert.Equal(["c", "a"], decision.Unmeasured.Select(opening => opening.UniqueId));
    }

    private static OpeningDecision Decide(SurfaceFacts slab, params CutterFacts[] cutters) =>
        SurfaceOpeningPolicy.Decide(slab, cutters);

    private static SurfaceFacts Slab(params HoleFacts[] holes) => new("floor", 1000.0, 1000.0, holes);

    /// <param name="generators">The elements across its edges; none when the host's own outline draws it.</param>
    private static HoleFacts Hole(double area, params string[] generators) => new(area, generators, DrawnByHost: generators.Length == 0);

    private static CutterFacts Cutter(string id, CutterKind kind = CutterKind.Opening) => new(id, kind, BeyondItsHoles: false);
}
