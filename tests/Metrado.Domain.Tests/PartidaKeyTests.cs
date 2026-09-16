namespace Metrado.Domain.Tests;

/// <summary>
/// A partida is keyed by capitulo plus the code the codification chain resolved,
/// and by nothing else. These tests pin that identity now, so the grouping step
/// cannot later quietly widen the key with a type or family discriminator.
/// </summary>
public sealed class PartidaKeyTests
{
    [Fact]
    public void KeyCarriesItsCapituloAndPartidaCode()
    {
        PartidaKey key = new("Estructuras", "C1010");

        Assert.Equal("Estructuras", key.Capitulo);
        Assert.Equal("C1010", key.PartidaCode);
    }

    /// <summary>
    /// The behaviour the grouping step depends on: two distinct wall types that
    /// resolve to the same code collapse into one partida.
    /// </summary>
    [Fact]
    public void TwoElementsResolvingToTheSameCodeInTheSameCapituloShareOneKey()
    {
        PartidaKey fromInteriorWall = new("Estructuras", "C1010");
        PartidaKey fromExteriorWall = new("Estructuras", "C1010");

        Assert.Equal(fromInteriorWall, fromExteriorWall);
        Assert.Equal(fromInteriorWall.GetHashCode(), fromExteriorWall.GetHashCode());
    }

    [Fact]
    public void KeysGroupIntoOneBucketPerDistinctCapituloAndCode()
    {
        Dictionary<PartidaKey, int> lineas = [];

        foreach (PartidaKey key in new PartidaKey[]
        {
            new("Estructuras", "C1010"),
            new("Estructuras", "C1010"),
            new("Estructuras", "C1020"),
            new("Arquitectura", "C1010"),
        })
        {
            lineas[key] = lineas.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        Assert.Equal(3, lineas.Count);
        Assert.Equal(2, lineas[new PartidaKey("Estructuras", "C1010")]);
        Assert.Equal(1, lineas[new PartidaKey("Arquitectura", "C1010")]);
    }

    [Fact]
    public void TheSameCodeInADifferentCapituloIsADifferentKey()
    {
        Assert.NotEqual(new PartidaKey("Estructuras", "C1010"), new PartidaKey("Arquitectura", "C1010"));
    }

    [Fact]
    public void ADifferentCodeInTheSameCapituloIsADifferentKey()
    {
        Assert.NotEqual(new PartidaKey("Estructuras", "C1010"), new PartidaKey("Estructuras", "C1020"));
    }

    /// <summary>
    /// A blank code would produce an unnamed partida in the budget. The chain
    /// treats whitespace-only codes as unresolved, and the key type makes the
    /// blank-named partida unrepresentable so that rule cannot be bypassed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPartidaCodeIsRejected(string? code)
    {
        Assert.Throws<ArgumentException>(() => new PartidaKey("Estructuras", code!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCapituloIsRejected(string? capitulo)
    {
        Assert.Throws<ArgumentException>(() => new PartidaKey(capitulo!, "C1010"));
    }

    /// <summary>
    /// <c>with</c> copies backing state directly, so a validating constructor
    /// alone would leave a hole. The copy path must validate too.
    /// </summary>
    [Fact]
    public void CopyingAKeyWithABlankCodeIsAlsoRejected()
    {
        PartidaKey key = new("Estructuras", "C1010");

        Assert.Throws<ArgumentException>(() => key with { PartidaCode = "  " });
    }
}
