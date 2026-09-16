namespace Metrado.Domain.Tests;

/// <summary>
/// <c>partida-codification</c>, requirement "Assembly Code Resolver": the first
/// link reads the element type's codification parameter, treats absent, empty and
/// whitespace-only readings as unresolved, and trims a code before use.
/// </summary>
public sealed class AssemblyCodeResolverTests
{
    [Fact]
    public void AWallTypeCarryingAnAssemblyCodeResolvesToThatCode()
    {
        string? resolved = new AssemblyCodeResolver().Resolve(CodificationFixture.WallCoded("C1010"));

        Assert.Equal("C1010", resolved);
    }

    /// <summary>
    /// The code comes off the element, so a second element carrying a different
    /// code resolves differently.
    /// </summary>
    [Fact]
    public void TheResolvedCodeIsTheOneTheElementCarriesNotAFixedOne()
    {
        string? resolved = new AssemblyCodeResolver().Resolve(CodificationFixture.WallCoded("B2010"));

        Assert.Equal("B2010", resolved);
    }

    /// <summary>
    /// Scenario "Whitespace-only code is not a code".
    /// </summary>
    /// <remarks>
    /// Real construction models are full of parameters someone tabbed through.
    /// Returning the spaces would hand <see cref="PartidaKey"/> a blank code, and
    /// a blank code is either a throw at grouping time or a partida with no name
    /// in the budget — never something the user asked for.
    /// </remarks>
    [Fact]
    public void AWhitespaceOnlyAssemblyCodeIsNotACode()
    {
        string? resolved = new AssemblyCodeResolver().Resolve(CodificationFixture.WallCoded("   "));

        Assert.Null(resolved);
    }

    /// <summary>
    /// "A resolved code MUST be trimmed before use."
    /// </summary>
    /// <remarks>
    /// Untrimmed, <c>" C1010 "</c> and <c>"C1010"</c> are two different partida
    /// keys, so one budget would carry the same partida twice with the quantities
    /// split between them.
    /// </remarks>
    [Fact]
    public void AResolvedCodeIsTrimmedBeforeUse()
    {
        string? resolved = new AssemblyCodeResolver().Resolve(CodificationFixture.WallCoded("  C1010  "));

        Assert.Equal("C1010", resolved);
    }

    /// <summary>
    /// The other two readings the requirement calls unresolved, plus the tab and
    /// newline a user can paste into a Revit parameter without seeing them.
    /// </summary>
    /// <param name="reading">
    /// <c>null</c> is the parameter being absent from the type altogether, which
    /// is a different fact from the parameter existing and holding nothing — the
    /// resolver owes the chain the same answer for both.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\t")]
    [InlineData(" \t\r\n ")]
    public void AnAbsentOrEmptyAssemblyCodeIsUnresolved(string? reading)
    {
        string? resolved = new AssemblyCodeResolver().Resolve(CodificationFixture.WallCoded(reading));

        Assert.Null(resolved);
    }
}
