namespace Metrado.Domain.Tests;

/// <summary>
/// The seam rule from <c>metrado-measurement</c>: every quantity crossing into
/// Domain declares its unit, and no imperial value is ever compared against a
/// metric threshold. Making the unit part of the value's identity is what turns
/// that requirement into something the type system enforces.
/// </summary>
public sealed class QuantityTests
{
    [Fact]
    public void QuantityUnitIsAClosedSetOfExactlyThreeValues()
    {
        QuantityUnit[] declared = Enum.GetValues<QuantityUnit>();

        Assert.Equal(3, declared.Length);
        Assert.Contains(QuantityUnit.SquareMetre, declared);
        Assert.Contains(QuantityUnit.CubicMetre, declared);
        Assert.Contains(QuantityUnit.Each, declared);
    }

    [Theory]
    [InlineData(QuantityUnit.SquareMetre, "m2")]
    [InlineData(QuantityUnit.CubicMetre, "m3")]
    [InlineData(QuantityUnit.Each, "u")]
    public void EachUnitCarriesTheSymbolTheSpecificationNames(QuantityUnit unit, string expected)
    {
        Assert.Equal(expected, unit.Symbol());
    }

    /// <summary>
    /// Zero is deliberately not a declared unit, so an uninitialized quantity
    /// cannot silently claim to be square metres.
    /// </summary>
    [Fact]
    public void ZeroIsNotADeclaredUnitSoAnUnsetUnitIsDetectable()
    {
        Assert.False(Enum.IsDefined(typeof(QuantityUnit), default(QuantityUnit)));
        Assert.Throws<ArgumentOutOfRangeException>(() => default(QuantityUnit).Symbol());
    }

    [Fact]
    public void QuantityCarriesItsValueAndUnit()
    {
        Quantity area = new(18.6, QuantityUnit.SquareMetre);

        Assert.Equal(18.6, area.Value);
        Assert.Equal(QuantityUnit.SquareMetre, area.Unit);
    }

    [Fact]
    public void QuantitiesWithTheSameValueAndUnitAreEqual()
    {
        Assert.Equal(new Quantity(18.0, QuantityUnit.SquareMetre), new Quantity(18.0, QuantityUnit.SquareMetre));
    }

    /// <summary>
    /// The whole point of tagging the unit: 18 m² and 18 m³ are different
    /// quantities, and nothing downstream may treat them as interchangeable.
    /// </summary>
    [Fact]
    public void TheSameNumberInDifferentUnitsIsADifferentQuantity()
    {
        Quantity area = new(18.0, QuantityUnit.SquareMetre);
        Quantity volume = new(18.0, QuantityUnit.CubicMetre);

        Assert.NotEqual(area, volume);
    }

    [Fact]
    public void QuantitiesWithDifferentValuesInTheSameUnitAreNotEqual()
    {
        Assert.NotEqual(
            new Quantity(18.0, QuantityUnit.SquareMetre),
            new Quantity(18.6, QuantityUnit.SquareMetre));
    }

    [Fact]
    public void QuantityRendersItsValueAndSymbol()
    {
        Assert.Equal("18.6 m2", new Quantity(18.6, QuantityUnit.SquareMetre).ToString());
    }

    /// <summary>
    /// A quantity whose unit was never set is exactly the value most likely to
    /// appear in a failure message, so rendering it must report the problem
    /// rather than throw a second exception on top of the first.
    /// </summary>
    [Fact]
    public void RenderingAQuantityWithAnUnsetUnitReportsItInsteadOfThrowing()
    {
        string rendered = default(Quantity).ToString();

        Assert.Contains("0", rendered, StringComparison.Ordinal);
        Assert.Contains("unit", rendered, StringComparison.OrdinalIgnoreCase);
    }
}
