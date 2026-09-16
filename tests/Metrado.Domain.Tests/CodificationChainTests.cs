namespace Metrado.Domain.Tests;

/// <summary>
/// <c>partida-codification</c>, requirement "Ordered Codification Resolver Chain":
/// the links are evaluated in a fixed order, the first non-empty code wins, later
/// links are not consulted, and the terminal link always succeeds so codification
/// cannot throw for an uncodeable element.
/// </summary>
public sealed class CodificationChainTests
{
    /// <summary>
    /// Scenario "Earlier link wins over later links".
    /// </summary>
    [Fact]
    public void TheEarlierLinkWinsAndTheLaterLinkIsNeverConsulted()
    {
        CodificationChain chain = new([
            new AssemblyCodeResolver(),
            CodificationFixture.LinkThatMustNotRun("Keynote"),
        ]);

        string resolved = chain.Resolve(
            CodificationFixture.WallRead(assemblyCode: "C1010", keynote: "M-030"));

        Assert.Equal("C1010", resolved);
    }

    /// <summary>
    /// A link that returns no match hands control on instead of ending the chain.
    /// </summary>
    /// <remarks>
    /// The later link is a test double, not a real Keynote resolver: reading the
    /// Keynote parameter is I2's task, and the ordering rule under test here is
    /// the chain's, not any particular link's.
    /// </remarks>
    [Fact]
    public void AnUnresolvedLinkPassesControlToTheNextOne()
    {
        CodificationChain chain = new([
            new AssemblyCodeResolver(),
            CodificationFixture.Link("M-030"),
        ]);

        string resolved = chain.Resolve(
            CodificationFixture.WallRead(assemblyCode: null, keynote: "M-030"));

        Assert.Equal("M-030", resolved);
    }

    /// <summary>
    /// Scenario "Chain terminates on an uncodeable element", and scenario "Wall
    /// with no assembly code" read through the chain.
    /// </summary>
    /// <remarks>
    /// The assertion doubles as the "no exception is raised" clause: a throw
    /// anywhere in the chain fails this test rather than returning a code.
    /// </remarks>
    [Fact]
    public void AnUncodeableElementResolvesAsUnclassifiedRatherThanThrowing()
    {
        CodificationChain chain = new([
            new AssemblyCodeResolver(),
            new UnclassifiedResolver(),
        ]);

        string resolved = chain.Resolve(
            CodificationFixture.WallRead(assemblyCode: null, keynote: null));

        Assert.Equal(UnclassifiedResolver.Code, resolved);
    }

    /// <summary>
    /// A chain with no terminal link is a misconfigured chain, and it says so.
    /// </summary>
    /// <remarks>
    /// The requirement's no-throw guarantee is about <em>uncodeable elements</em>:
    /// with the terminal link in place no element can exhaust the chain, so the
    /// guarantee holds. It is not a promise to paper over a chain that was
    /// assembled without the link that makes it total. Quietly substituting the
    /// terminal the caller never supplied would make a wiring bug indistinguishable
    /// from a correctly wired chain, and every element in the model would land in
    /// the unclassified block with nothing to explain why.
    /// </remarks>
    [Fact]
    public void AChainAssembledWithoutATerminalLinkRefusesInsteadOfInventingOne()
    {
        CodificationChain chain = new([new AssemblyCodeResolver()]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => chain.Resolve(CodificationFixture.WallRead(assemblyCode: null, keynote: null)));

        Assert.Contains(nameof(UnclassifiedResolver), error.Message);
    }

    /// <summary>
    /// Scenario "Whitespace-only code is not a code", read end to end.
    /// </summary>
    /// <remarks>
    /// The second assertion is the one that matters. <see cref="PartidaKey"/>
    /// rejects a blank code, so a chain that passed the spaces through would not
    /// merely name a partida badly — it would throw at grouping time, turning a
    /// tabbed-through parameter into a failed export. Building the key here proves
    /// the code the chain hands downstream is one the budget can actually carry.
    /// </remarks>
    [Fact]
    public void AWhitespaceOnlyCodeLandsInTheUnclassifiedGroupNotInABlankNamedPartida()
    {
        CodificationChain chain = new([
            new AssemblyCodeResolver(),
            new UnclassifiedResolver(),
        ]);

        string resolved = chain.Resolve(CodificationFixture.WallCoded("   "));

        Assert.Equal(UnclassifiedResolver.Code, resolved);
        Assert.Equal(UnclassifiedResolver.Code, new PartidaKey("Walls", resolved).PartidaCode);
    }

    /// <summary>
    /// Order is decided by position in the chain, not by which link answered.
    /// </summary>
    /// <remarks>
    /// Both links resolve here, so the assertion discriminates first-wins from
    /// last-wins by value alone, without relying on a link that refuses to run.
    /// </remarks>
    [Fact]
    public void TheFirstResolvingLinkWinsEvenWhenALaterLinkAlsoHasACode()
    {
        CodificationChain chain = new([
            CodificationFixture.Link("FIRST"),
            CodificationFixture.Link("SECOND"),
        ]);

        string resolved = chain.Resolve(CodificationFixture.WallCoded(null));

        Assert.Equal("FIRST", resolved);
    }

    /// <summary>
    /// "The first resolver returning a non-empty code SHALL win" — so a link that
    /// answers with blank text has not won anything.
    /// </summary>
    /// <remarks>
    /// The chain enforces this itself rather than trusting each link to. The
    /// Assembly Code link already trims and declines blanks, but the chain's later
    /// slots are reserved for links this increment does not own — Keynote, shared
    /// parameter, and the I4 rule resolver, which is user-supplied. A blank
    /// escaping from any of them reaches <see cref="PartidaKey"/> and throws
    /// during grouping, so the guarantee belongs where every link passes through.
    /// </remarks>
    [Fact]
    public void ALinkAnsweringWithBlankTextHasNotResolvedACode()
    {
        CodificationChain chain = new([
            CodificationFixture.Link("   "),
            CodificationFixture.Link("C1010"),
        ]);

        string resolved = chain.Resolve(CodificationFixture.WallCoded(null));

        Assert.Equal("C1010", resolved);
    }

    /// <summary>
    /// The terminal link resolves whatever it is handed, which is what makes the
    /// chain total.
    /// </summary>
    /// <param name="assemblyCode">
    /// Varied to show the terminal link does not inspect the element at all. It is
    /// last precisely because every link that reads something has already declined.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("C1010")]
    public void TheTerminalLinkResolvesEveryElementItIsGiven(string? assemblyCode)
    {
        string? resolved = new UnclassifiedResolver().Resolve(
            CodificationFixture.WallCoded(assemblyCode));

        Assert.Equal(UnclassifiedResolver.Code, resolved);
    }
}
