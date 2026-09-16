namespace Metrado.Domain.Tests;

/// <summary>
/// <see cref="ConfigError"/> is the only thing that stops a run, so it has to
/// carry enough to point the user at the exact entry to fix: which file, where in
/// it, which category, and what the offending value was.
/// </summary>
public sealed class ConfigErrorTests
{
    [Fact]
    public void AnErrorAlwaysCarriesAMessage()
    {
        Assert.Equal("Threshold must not be negative.", new ConfigError("Threshold must not be negative.").Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankMessageIsRejected(string? message)
    {
        Assert.Throws<ArgumentException>(() => new ConfigError(message!));
    }

    /// <summary>
    /// The <c>takeoff-configuration</c> MUST: "the error message identifies the
    /// file and the failing location".
    /// </summary>
    [Fact]
    public void AParseErrorIdentifiesTheFileAndTheFailingLocation()
    {
        ConfigError error = new("Invalid JSON syntax.")
        {
            FilePath = "/Users/estimator/criteria.json",
            Location = new ConfigLocation(line: 12, position: 34),
        };

        Assert.Equal("/Users/estimator/criteria.json", error.FilePath);
        Assert.Equal(12, error.Location!.Line);
        Assert.Equal(34, error.Location.Position);
    }

    /// <summary>
    /// The <c>takeoff-configuration</c> MUST: "loading fails with an error naming
    /// the category and the invalid value".
    /// </summary>
    [Fact]
    public void AValueErrorNamesTheOffendingCategoryAndTheInvalidValue()
    {
        ConfigError error = new("Openings threshold must not be negative.")
        {
            Category = "Walls",
            InvalidValue = "-1",
        };

        Assert.Equal("Walls", error.Category);
        Assert.Equal("-1", error.InvalidValue);
    }

    /// <summary>
    /// Every field beyond the message is optional, because an error raised by a
    /// value object has no file and no position to report.
    /// </summary>
    [Fact]
    public void AnErrorRaisedAwayFromAFileCarriesNoFileOrLocation()
    {
        ConfigError error = new("Openings threshold must not be negative.");

        Assert.Null(error.FilePath);
        Assert.Null(error.Location);
        Assert.Null(error.Category);
        Assert.Null(error.InvalidValue);
    }

    [Fact]
    public void ANegativeLineOrPositionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigLocation(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigLocation(0, -1));
    }

    /// <summary>
    /// <see cref="ConfigError"/> lives in Domain because
    /// <c>OpeningsThreshold.TryCreate</c> returns it. Domain has a zero
    /// third-party closure (decision D1) and multi-targets netstandard2.0, where
    /// System.Text.Json is a NuGet package dragging five more assemblies in.
    /// <para>
    /// Carrying the failing location as plain numbers rather than a
    /// <c>JsonException</c> is what keeps that true. The compiler emits an
    /// assembly reference the moment any Domain code touches a type from another
    /// assembly, so the reference list is structural proof rather than a promise.
    /// </para>
    /// </summary>
    [Fact]
    public void DomainDoesNotReferenceSystemTextJson()
    {
        string[] referenced = [.. typeof(ConfigError).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)];

        Assert.DoesNotContain("System.Text.Json", referenced);
    }

    [Fact]
    public void DomainReferencesOnlyFrameworkAssemblies()
    {
        string[] nonFramework = [.. typeof(ConfigError).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                && name is not ("System" or "netstandard" or "mscorlib"))];

        Assert.Empty(nonFramework);
    }
}
