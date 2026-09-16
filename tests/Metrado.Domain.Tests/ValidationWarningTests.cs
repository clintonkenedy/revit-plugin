namespace Metrado.Domain.Tests;

/// <summary>
/// The <c>model-validation-warnings</c> MUST: each warning identifies the
/// offending element by <c>UniqueId</c>, its category, family and type, and
/// states the condition detected. A warning that cannot be traced back to an
/// element is not an instruction the user can act on.
/// </summary>
public sealed class ValidationWarningTests
{
    private static ElementTakeoff Wall(string uniqueId = "wall-1") =>
        new(
            UniqueId: uniqueId,
            CategoryName: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeKey: "Basic Wall:Generic - 200mm",
            Codes: new CodificationReadings(null, null, new Dictionary<string, string?>()),
            Quantities: [],
            Openings: []);

    [Fact]
    public void AWarningCarriesEveryPayloadFieldTheSpecificationRequires()
    {
        ValidationWarning warning = new(
            uniqueId: "abc-123",
            categoryName: "Walls",
            familyName: "Basic Wall",
            typeName: "Generic - 200mm",
            condition: "Openings exceed the gross quantity.");

        Assert.Equal("abc-123", warning.UniqueId);
        Assert.Equal("Walls", warning.CategoryName);
        Assert.Equal("Basic Wall", warning.FamilyName);
        Assert.Equal("Generic - 200mm", warning.TypeName);
        Assert.Equal("Openings exceed the gross quantity.", warning.Condition);
    }

    /// <summary>
    /// Building the warning from the element it describes is what stops the four
    /// identity fields from drifting apart from the element at any call site.
    /// </summary>
    [Fact]
    public void AWarningBuiltFromAnElementInheritsThatElementsIdentity()
    {
        ValidationWarning warning = ValidationWarning.ForElement(
            Wall("abc-123"),
            "Metrado was clamped to the gross quantity.");

        Assert.Equal("abc-123", warning.UniqueId);
        Assert.Equal("Walls", warning.CategoryName);
        Assert.Equal("Basic Wall", warning.FamilyName);
        Assert.Equal("Generic - 200mm", warning.TypeName);
        Assert.Equal("Metrado was clamped to the gross quantity.", warning.Condition);
    }

    [Fact]
    public void TwoWarningsAboutDifferentElementsAreDistinct()
    {
        ValidationWarning first = ValidationWarning.ForElement(Wall("wall-1"), "Zero metrado.");
        ValidationWarning second = ValidationWarning.ForElement(Wall("wall-2"), "Zero metrado.");

        Assert.NotEqual(first, second);
        Assert.Equal(first, ValidationWarning.ForElement(Wall("wall-1"), "Zero metrado."));
    }

    /// <summary>
    /// An unattributable warning is noise: the user cannot find the element to
    /// fix, and a warning with no condition states nothing at all.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankUniqueIdIsRejected(string? uniqueId)
    {
        Assert.Throws<ArgumentException>(
            () => new ValidationWarning(uniqueId!, "Walls", "Basic Wall", "Generic - 200mm", "Zero metrado."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankConditionIsRejected(string? condition)
    {
        Assert.Throws<ArgumentException>(
            () => new ValidationWarning("abc-123", "Walls", "Basic Wall", "Generic - 200mm", condition!));
    }

    [Fact]
    public void ABlankCategoryFamilyOrTypeIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new ValidationWarning("abc-123", "  ", "Basic Wall", "Generic - 200mm", "Zero metrado."));
        Assert.Throws<ArgumentException>(
            () => new ValidationWarning("abc-123", "Walls", "  ", "Generic - 200mm", "Zero metrado."));
        Assert.Throws<ArgumentException>(
            () => new ValidationWarning("abc-123", "Walls", "Basic Wall", "  ", "Zero metrado."));
    }
}
