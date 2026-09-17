namespace Metrado.Domain.Tests;

/// <summary>
/// <c>takeoff-configuration</c>, requirement "Usable Defaults Without Any
/// Configuration": "the run reports which criteria, threshold values and boundary
/// modes were actually applied", and the "Missing file falls back to defaults"
/// scenario adds "the run reports that no configuration file was found". These
/// tests pin the value that carries both facts, so the completion report cannot
/// name a file whose contents were never honoured, nor honour one it cannot name.
/// </summary>
public sealed class EffectiveCriteriaTests
{
    [Fact]
    public void ConfigSourceIsAClosedSetOfExactlyTwoValues()
    {
        ConfigSource[] declared = Enum.GetValues<ConfigSource>();

        Assert.Equal(2, declared.Length);
        Assert.Contains(ConfigSource.BuiltInDefaults, declared);
        Assert.Contains(ConfigSource.File, declared);
    }

    /// <summary>
    /// Zero names no source, so criteria whose provenance was never set are
    /// detectable instead of silently reporting whichever member came first.
    /// </summary>
    [Fact]
    public void ZeroIsNotADeclaredConfigSource()
    {
        Assert.False(Enum.IsDefined(typeof(ConfigSource), default(ConfigSource)));
    }

    /// <summary>
    /// Members carry explicit values, matching every other enum in this assembly,
    /// so reordering the declaration cannot change what any of them means.
    /// </summary>
    [Fact]
    public void ConfigSourceMemberValuesArePinnedAgainstReordering()
    {
        Assert.Equal(1, (int)ConfigSource.BuiltInDefaults);
        Assert.Equal(2, (int)ConfigSource.File);
    }

    /// <summary>
    /// A run that used no file reports exactly that, and names none.
    /// </summary>
    [Fact]
    public void ADefaultsRunReportsTheBuiltInSourceAndNamesNoFile()
    {
        EffectiveCriteria effective = new(CriteriaSet.Default, ConfigSource.BuiltInDefaults, path: null);

        Assert.Equal(ConfigSource.BuiltInDefaults, effective.Source);
        Assert.Null(effective.Path);
        Assert.Same(CriteriaSet.Default, effective.Criteria);
    }

    /// <summary>
    /// The other direction: a run driven by a file reports the file it was read
    /// from, so the estimator can tell which of several criteria files was picked
    /// up.
    /// </summary>
    [Fact]
    public void AFileRunReportsTheFileItsCriteriaCameFrom()
    {
        EffectiveCriteria effective = new(
            CriteriaSet.Default,
            ConfigSource.File,
            "/Users/estimator/metrado/criteria.json");

        Assert.Equal(ConfigSource.File, effective.Source);
        Assert.Equal("/Users/estimator/metrado/criteria.json", effective.Path);
    }

    [Fact]
    public void CriteriaAreRequiredBecauseAnEffectiveSetWithNoCriteriaMeasuresNothing()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new EffectiveCriteria(null!, ConfigSource.BuiltInDefaults, path: null));

        Assert.Equal("criteria", refused.ParamName);
    }

    /// <summary>
    /// A file in force must name itself. The completion report has to say which
    /// configuration was applied, and "criteria came from a file" without the file
    /// is an answer the estimator cannot act on — there is typically more than one
    /// candidate path.
    /// </summary>
    [Fact]
    public void AFileInForceMustNameItselfRatherThanReportAnUnnamedFile()
    {
        Assert.Throws<ArgumentException>(
            () => new EffectiveCriteria(CriteriaSet.Default, ConfigSource.File, path: null));
    }

    /// <summary>
    /// The inverse confusion, and the more dangerous one: defaults carrying a path
    /// would let the completion dialog read "criteria from criteria.json" for a run
    /// that honoured no file at all. That is the silent-fallback report
    /// <c>takeoff-configuration</c> forbids, wearing a filename.
    /// </summary>
    [Fact]
    public void TheBuiltInDefaultsCannotCarryAPathBecauseNoFileWasHonoured()
    {
        Assert.Throws<ArgumentException>(
            () => new EffectiveCriteria(
                CriteriaSet.Default,
                ConfigSource.BuiltInDefaults,
                "/Users/estimator/metrado/criteria.json"));
    }

    /// <summary>
    /// An undeclared source is refused at construction rather than carried into a
    /// report that would have to render it as a bare number.
    /// </summary>
    [Fact]
    public void AnUndeclaredConfigSourceIsRefusedRatherThanReported()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EffectiveCriteria(CriteriaSet.Default, (ConfigSource)7, path: null));
    }
}
