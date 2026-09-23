using static Metrado.Domain.Tests.CodificationFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// The codification chain's second and third links (task 2.4):
/// <c>partida-codification</c>, "Keynote Resolver" and "Shared Parameter
/// Resolver", each applying the Assembly Code link's rule (a value is trimmed,
/// then judged; empty or whitespace is no code), in the chain's fixed order.
/// </summary>
public sealed class SecondAndThirdLinkTests
{
    private const string Nominated = "Metrado_Partida";

    /// <summary>"Keynote used as fallback".</summary>
    [Fact]
    public void AnEmptyAssemblyCodeFallsBackToTheKeynote()
    {
        Assert.Equal("M-030", CodificationChain.Standard(sharedParameter: null).Resolve(WallRead(assemblyCode: "", keynote: "M-030")));
    }

    /// <summary>"Earlier link wins over later links".</summary>
    [Fact]
    public void AnAssemblyCodeWinsOverTheKeynote()
    {
        Assert.Equal("C1010", CodificationChain.Standard(sharedParameter: null).Resolve(WallRead(assemblyCode: "C1010", keynote: "M-030")));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("\tM-030 ", "M-030")]
    public void TheKeynoteIsTrimmedThenJudged(string? keynote, string? expected)
    {
        Assert.Equal(expected, new KeynoteResolver().Resolve(WallRead(assemblyCode: null, keynote: keynote)));
    }

    /// <summary>
    /// "Shared parameter not bound in the model": the resolver answers
    /// "unresolved" for every element, raises nothing, and the chain goes on
    /// to the next link, here the terminal one.
    /// </summary>
    [Fact]
    public void ASharedParameterTheModelDoesNotDefineLeavesEveryElementToTheNextLink()
    {
        CodificationChain chain = CodificationChain.Standard(sharedParameter: Nominated);
        ElementTakeoff[] elements = [WallRead(null, null), WallRead("  ", ""), WithShared("Other_Parameter", "X-1")];

        Assert.All(elements, element => Assert.Null(new SharedParameterResolver(Nominated).Resolve(element)));
        Assert.All(elements, element => Assert.Equal(UnclassifiedResolver.Code, chain.Resolve(element)));
    }

    [Theory]
    [InlineData(" S-100 ", "S-100")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void ANominatedSharedParameterIsTrimmedThenJudged(string? value, string? expected)
    {
        Assert.Equal(expected, new SharedParameterResolver(Nominated).Resolve(WithShared(Nominated, value)));
    }

    /// <summary>The shared parameter is third: an Assembly Code or a Keynote still wins over it.</summary>
    [Fact]
    public void TheSharedParameterComesAfterTheKeynote()
    {
        ElementTakeoff element = WithShared(Nominated, "S-100") with
        {
            Codes = new CodificationReadings(assemblyCode: null, keynote: "M-030", new Dictionary<string, string?> { [Nominated] = "S-100" }),
        };

        Assert.Equal("M-030", CodificationChain.Standard(sharedParameter: Nominated).Resolve(element));
        Assert.Equal("S-100", CodificationChain.Standard(sharedParameter: Nominated).Resolve(WithShared(Nominated, "S-100")));
    }

    /// <summary>No parameter nominated, no third link: a shared value is never read by accident.</summary>
    [Fact]
    public void WithNoNominatedParameterTheChainReadsNoSharedValue()
    {
        Assert.Equal(UnclassifiedResolver.Code, CodificationChain.Standard(sharedParameter: null).Resolve(WithShared(Nominated, "S-100")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankNominationIsRefused(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SharedParameterResolver(name));
    }

    private static ElementTakeoff WithShared(string name, string? value) =>
        WallRead(assemblyCode: null, keynote: null) with
        {
            Codes = new CodificationReadings(assemblyCode: null, keynote: null, new Dictionary<string, string?> { [name] = value }),
        };
}
