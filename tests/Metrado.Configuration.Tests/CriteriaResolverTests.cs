using Metrado.Domain;

namespace Metrado.Configuration.Tests;

/// <summary>
/// <c>takeoff-configuration</c> puts two obligations on resolution that pull in
/// opposite directions. "Usable Defaults Without Any Configuration": the add-in
/// "SHALL run correctly with no configuration file present" and "Absence of
/// configuration MUST NOT be an error". "Invalid Configuration Fails Loudly": the
/// system "MUST NOT silently fall back to defaults when a file was supplied,
/// because a silently ignored configuration produces a confidently wrong budget".
/// <para>
/// Resolution is the single point where both are decided, so these tests are
/// written as the pair: every case that must fall back, and every case that must
/// stop.
/// </para>
/// </summary>
public sealed class CriteriaResolverTests
{
    private const string CriteriaPath = "/Users/estimator/metrado/criteria.json";

    [Fact]
    public void NoCriteriaFileMeasuresUnderTheBuiltInDefaultsAndIsNotAnError()
    {
        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(CriteriaFileLookup.Absent, path: null);

        Assert.True(
            resolved.IsOk,
            "A first run with no criteria file was reported as a configuration error, "
                + "but absence of configuration MUST NOT be an error.");
        Assert.Equal(ConfigSource.BuiltInDefaults, resolved.Value.Source);
    }

    /// <summary>
    /// The defaults handed back are the product's own table, not an empty set that
    /// would measure nothing while reporting success.
    /// </summary>
    [Fact]
    public void TheDefaultsThatComeBackAreTheProductsOwnCriteriaSet()
    {
        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(CriteriaFileLookup.Absent, path: null);

        Assert.Same(CriteriaSet.Default, resolved.Value.Criteria);
        Assert.True(
            resolved.Value.Criteria.ByCategory.ContainsKey("Walls"),
            "The built-in criteria came back without the Walls criterion, so an I1 run "
                + "would measure no walls while reporting a successful export.");
    }

    /// <summary>
    /// A caller knows the path it probed, and it passes that path in. It must not
    /// come back out as the provenance of criteria that came from no file — the
    /// completion report would then read "criteria from criteria.json" for a run
    /// that honoured none.
    /// </summary>
    [Fact]
    public void TheProbedPathIsNotReportedAsTheSourceOfDefaultsThatCameFromNoFile()
    {
        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(CriteriaFileLookup.Absent, CriteriaPath);

        Assert.True(resolved.IsOk, "Probing a path that held no file is still no file.");
        Assert.Equal(ConfigSource.BuiltInDefaults, resolved.Value.Source);
        Assert.Null(resolved.Value.Path);
    }

    /// <summary>
    /// The load-bearing case, and the reason the locator has three states rather
    /// than two (residual finding N3). A file that is there and cannot be read must
    /// stop the run. Falling back here is the silent fallback the specification
    /// forbids outright, and it is invisible: the export succeeds and every quantity
    /// in the workbook was measured under criteria the estimator replaced.
    /// </summary>
    [Fact]
    public void AnUnreadableFileStopsTheRunAndNeverFallsBackToDefaults()
    {
        CriteriaFileLookup locked = CriteriaFileLookup.Unreadable(
            new ConfigError("The criteria file is open in another process.")
            {
                FilePath = CriteriaPath,
            });

        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(locked, CriteriaPath);

        Assert.False(
            resolved.IsOk,
            "A criteria file that exists but could not be read resolved successfully. "
                + "If it resolved to the built-in defaults, this is exactly the silently "
                + "ignored configuration that produces a confidently wrong budget.");
    }

    /// <summary>
    /// And the error that stops it is the locator's own, so the file stays named.
    /// Only the locator knows which path it actually tried to open; re-deriving that
    /// here could name a different file than the one that failed.
    /// </summary>
    [Fact]
    public void AnUnreadableFileKeepsTheLocatorsOwnErrorSoTheFileStaysNamed()
    {
        ConfigError fromLocator = new("Access to the criteria file was denied.")
        {
            FilePath = CriteriaPath,
        };

        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(CriteriaFileLookup.Unreadable(fromLocator), CriteriaPath);

        Assert.Same(fromLocator, resolved.Error);
    }

    [Fact]
    public void AFileThatWasReadIsInForceAndNamed()
    {
        EffectiveCriteria criteria = Resolved("{ \"Walls\": { \"threshold\": 2.5, \"mode\": \"inclusive\" } }");

        Assert.Equal(ConfigSource.File, criteria.Source);
        Assert.Equal(CriteriaPath, criteria.Path);
        Assert.Equal(2.5, criteria.Criteria.ByCategory["Walls"].Threshold.Value);
        Assert.Equal(BoundaryMode.Inclusive, criteria.Criteria.ByCategory["Walls"].Threshold.Mode);
    }

    /// <summary>A file that overrides nothing is still the source named, with every default kept.</summary>
    [Fact]
    public void AFileWithNoEntriesKeepsTheDefaultsButIsNamed()
    {
        EffectiveCriteria criteria = Resolved("{ }");

        Assert.Equal(ConfigSource.File, criteria.Source);
        Assert.Equal(CriteriaSet.Default.ByCategory["Walls"], criteria.Criteria.ByCategory["Walls"]);
    }

    /// <summary>
    /// <c>takeoff-configuration</c>, "Malformed configuration file": the run stops
    /// and "the error message identifies the file and the failing location".
    /// </summary>
    [Fact]
    public void MalformedSyntaxStopsNamingTheFileAndTheLine()
    {
        ConfigError error = Stopped("{\n  \"Walls\": { \"threshold\": }\n}");

        Assert.Equal(CriteriaPath, error.FilePath);
        Assert.Equal(2, error.Location?.Line);
        Assert.StartsWith($"The criteria file '{CriteriaPath}', line 2, position ", error.Message);
    }

    /// <summary>"Unknown category in configuration": the run stops naming the category, at the line it is on.</summary>
    [Fact]
    public void AnUnknownCategoryStopsNamingItAndItsLine()
    {
        ConfigError error = Stopped("{\n  \"Wals\": { \"threshold\": 1 }\n}");

        Assert.Equal("Wals", error.Category);
        Assert.Equal(new ConfigLocation(2, 3), error.Location);
        Assert.Contains("'Wals' is not a category", error.Message);
        Assert.StartsWith($"The criteria file '{CriteriaPath}', line 2, position 3: ", error.Message);
    }

    /// <summary>"Negative threshold is rejected": the error names the category and the value.</summary>
    [Fact]
    public void ANegativeThresholdStopsNamingItsCategoryValueAndLine()
    {
        ConfigError error = Stopped("{ \"Walls\": { \"threshold\": -1 } }");

        Assert.Equal("Walls", error.Category);
        Assert.Equal("-1", error.InvalidValue);
        Assert.Equal(new ConfigLocation(1, 3), error.Location);
        Assert.Contains("'Walls'", error.Message);
        Assert.Contains("-1", error.Message);
    }

    /// <summary>A category written twice is pointed at where it is written the second time.</summary>
    [Fact]
    public void ARepeatedCategoryPointsAtTheRepetition()
    {
        ConfigError error = Stopped("{\n  \"Walls\": {},\n  \"Walls\": { \"threshold\": 2 }\n}");

        Assert.Equal(new ConfigLocation(3, 3), error.Location);
    }

    /// <summary>
    /// Triangulation on the same branch with the path absent: the run still stops.
    /// A file read without its name cannot be named as the source of the criteria
    /// in force, and criteria from an unnamed file are the silent fallback again.
    /// </summary>
    [Fact]
    public void AReadFileWithNoPathStillStopsTheRunEvenThoughItCannotBeNamed()
    {
        Result<EffectiveCriteria, ConfigError> resolved =
            CriteriaResolver.Resolve(CriteriaFileLookup.Found("{ }"), path: null);

        Assert.False(resolved.IsOk);
        Assert.Null(resolved.Error.FilePath);
    }

    /// <summary>
    /// A missing lookup is a caller defect, not a reading. Treating it as "no file"
    /// would turn a bug in the locator into a silent run under the defaults.
    /// </summary>
    [Fact]
    public void AMissingLookupIsRefusedRatherThanTreatedAsNoFile()
    {
        ArgumentNullException refused = Assert.Throws<ArgumentNullException>(
            () => CriteriaResolver.Resolve(null!, CriteriaPath));

        Assert.Equal("lookup", refused.ParamName);
    }

    private static EffectiveCriteria Resolved(string text)
    {
        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(CriteriaFileLookup.Found(text), CriteriaPath);
        Assert.True(resolved.IsOk, resolved.IsOk ? null : resolved.Error.Message);
        return resolved.Value;
    }

    private static ConfigError Stopped(string text)
    {
        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(CriteriaFileLookup.Found(text), CriteriaPath);
        Assert.False(resolved.IsOk, "The file was honoured.");
        return resolved.Error;
    }
}
