namespace Metrado.Domain.Tests;

/// <summary>
/// The boundary mode decides whether an opening exactly equal to the threshold is
/// added back, and the two answers are both defensible under different metrado
/// norms. That makes the default a product convention, not an implementation
/// detail: if it flips silently, one model yields two different budgets with
/// nothing in the output to tell them apart.
/// </summary>
public sealed class BoundaryModeTests
{
    [Fact]
    public void BoundaryModeIsAClosedSetOfExactlyTwoValues()
    {
        BoundaryMode[] declared = Enum.GetValues<BoundaryMode>();

        Assert.Equal(2, declared.Length);
        Assert.Contains(BoundaryMode.Exclusive, declared);
        Assert.Contains(BoundaryMode.Inclusive, declared);
    }

    /// <summary>
    /// Zero is deliberately undeclared, so a mode that was never set is
    /// detectable instead of silently meaning whichever member happens to be
    /// first.
    /// </summary>
    [Fact]
    public void ZeroIsNotADeclaredBoundaryMode()
    {
        Assert.False(Enum.IsDefined(typeof(BoundaryMode), default(BoundaryMode)));
    }

    /// <summary>
    /// This is the test that fails if the default is ever taken from the ordinal.
    /// Writing <c>Defaults.Mode = default</c> or <c>(BoundaryMode)0</c> — the
    /// usual ways an ordinal creeps in — produces an undeclared value, and this
    /// assertion rejects it.
    /// </summary>
    [Fact]
    public void TheDefaultModeIsADeclaredMemberNotAnOrdinal()
    {
        Assert.True(
            Enum.IsDefined(typeof(BoundaryMode), Defaults.Mode),
            $"Defaults.Mode is {(int)Defaults.Mode}, which is not a declared BoundaryMode. "
                + "The default must name a member, never an ordinal.");
    }

    /// <summary>
    /// The default is pinned by name. Resolving the expectation through
    /// <see cref="Enum.Parse{T}(string)"/> means this test asserts against the
    /// member called "Exclusive", not against whatever sits at a given position.
    /// </summary>
    [Fact]
    public void TheDefaultModeIsExclusiveResolvedByName()
    {
        Assert.Equal(Enum.Parse<BoundaryMode>("Exclusive"), Defaults.Mode);
    }

    /// <summary>
    /// Members carry explicit values, so reordering the declaration cannot change
    /// what any of them means. This assertion fails the moment someone deletes
    /// those assignments and lets position decide again.
    /// </summary>
    [Fact]
    public void MemberValuesArePinnedSoReorderingTheDeclarationCannotChangeThem()
    {
        Assert.Equal(1, (int)BoundaryMode.Exclusive);
        Assert.Equal(2, (int)BoundaryMode.Inclusive);
    }

    /// <summary>
    /// Ordering by numeric value is how <see cref="Enum.GetValues{T}"/> reports
    /// members, so pinning the values also pins that order. A default read as
    /// "the first member" therefore cannot drift either.
    /// </summary>
    [Fact]
    public void TheLowestValuedMemberIsExclusiveSoEvenAPositionalReadStaysCorrect()
    {
        Assert.Equal(BoundaryMode.Exclusive, Enum.GetValues<BoundaryMode>().Min());
    }
}
