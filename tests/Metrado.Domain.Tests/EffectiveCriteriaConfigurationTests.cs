namespace Metrado.Domain.Tests;

/// <summary>
/// The criteria in force name the saved configuration they came from, and
/// its shared parameter, only when they came from a file: the product's own
/// criteria are no configuration, and naming one would be read out as a lie.
/// </summary>
public sealed class EffectiveCriteriaConfigurationTests
{
    [Fact]
    public void CriteriaFromAFileMayNameTheirConfiguration()
    {
        Guid shared = Guid.NewGuid();

        EffectiveCriteria criteria = new(CriteriaSet.Default, ConfigSource.File, "C:/model/metrado.criteria.json") { ConfigurationName = "Obra", SharedParameter = shared };

        Assert.Equal(("Obra", (Guid?)shared), (criteria.ConfigurationName, criteria.SharedParameter));
    }

    [Fact]
    public void TheBuiltInCriteriaNameNoConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new EffectiveCriteria(CriteriaSet.Default, ConfigSource.BuiltInDefaults, path: null) { ConfigurationName = "Obra" });
        Assert.Throws<ArgumentException>(() => new EffectiveCriteria(CriteriaSet.Default, ConfigSource.BuiltInDefaults, path: null) { SharedParameter = Guid.NewGuid() });
    }
}
