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

    /// <summary>
    /// I1 ships no parser: the "External, Versionable Criteria File" requirement is
    /// tagged I2, and task 2.1 builds the reader. So a file that was read is an input
    /// I1 cannot honour, and the specification leaves exactly one response — the
    /// system "MUST NOT silently fall back to defaults when a file was supplied", so
    /// the run stops.
    /// </summary>
    [Fact]
    public void AFileThatWasReadStopsTheRunBecauseThisIncrementCannotHonourItYet()
    {
        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(
            CriteriaFileLookup.Found("{ \"Walls\": { \"threshold\": 2.5 } }"),
            CriteriaPath);

        Assert.False(
            resolved.IsOk,
            "A criteria file was supplied and read, and resolution ignored it and "
                + "returned criteria anyway. The estimator's thresholds would be absent "
                + "from a budget that reports itself as exported successfully.");
    }

    /// <summary>
    /// The refusal names the file, and deliberately claims no position inside it.
    /// </summary>
    /// <remarks>
    /// Nothing was parsed, so there is no failing location. Reporting one — line 0,
    /// position 0, the values a default-constructed location would carry — would send
    /// the estimator to hunt a syntax error that does not exist in a file that is
    /// probably perfectly valid.
    /// </remarks>
    [Fact]
    public void TheRefusalOfAReadFileNamesTheFileAndClaimsNoPositionInsideIt()
    {
        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(
            CriteriaFileLookup.Found("{ }"),
            CriteriaPath);

        Assert.Equal(CriteriaPath, resolved.Error.FilePath);
        Assert.Null(resolved.Error.Location);
    }

    /// <summary>
    /// Triangulation on the same branch with the path absent: the run still stops.
    /// A caller that read a file without retaining its name has still supplied a
    /// configuration, and honouring it is still impossible.
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
}
