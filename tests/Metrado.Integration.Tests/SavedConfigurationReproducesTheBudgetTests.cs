using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// <c>takeoff-configuration</c>, "Saved, Reusable Configurations" (task 3.6),
/// run through the export's own pass: a configuration saved and reloaded
/// reproduces every metrado; two that differ only in the wall threshold
/// differ only by the openings between the two; one saved with roofs layered
/// runs on a model with no roofs.
/// </summary>
public sealed class SavedConfigurationReproducesTheBudgetTests
{
    private const string Path = "C:/configurations/obra.metrado.json";

    [Fact]
    public void AReloadedConfigurationReproducesEveryMetrado()
    {
        SavedConfiguration written = new("Obra Los Olivos", Walls(threshold: 0.5, layered: true), Guid.Parse("4f46423f-5c26-11d4-9217-0000863f27ad"));
        List<ElementTakeoff> model = [.. ModelFixture.Walls, ModelFixture.LayeredWall("w-layered", wholeVolumeOver: 0)];

        using ExportRun first = ExportPipeline.Run(InForce(written), model);
        using ExportRun second = ExportPipeline.Run(InForce(Reloaded(written)), model);

        Assert.Equal(Lines(first), Lines(second));
        Assert.Contains(Lines(first), line => line.Material == "Ladrillo");
        Assert.Equal(first.Report.Warnings, second.Report.Warnings);
    }

    /// <summary>At 1 m2 and at 3 m2 each wall differs by exactly its openings from 1 up to 3 m2, which only the second adds back.</summary>
    [Fact]
    public void TwoThresholdsDifferOnlyByTheOpeningsBetweenThem()
    {
        using ExportRun one = ExportPipeline.Run(InForce(Reloaded(new SavedConfiguration("one", Walls(threshold: 1.0, layered: false), null))), ModelFixture.Walls);
        using ExportRun three = ExportPipeline.Run(InForce(Reloaded(new SavedConfiguration("three", Walls(threshold: 3.0, layered: false), null))), ModelFixture.Walls);

        Assert.All(ModelFixture.Walls, wall =>
        {
            double between = wall.Openings.Where(opening => opening.Amount.Value is >= 1.0 and < 3.0).Sum(opening => opening.Amount.Value);
            Assert.Equal(between, three.LineaOf(wall.UniqueId).Metrado.Metrado.Value - one.LineaOf(wall.UniqueId).Metrado.Metrado.Value, 9);
            Assert.Equal(one.LineaOf(wall.UniqueId).PartidaCode, three.LineaOf(wall.UniqueId).PartidaCode);
        });
        Assert.Contains(ModelFixture.Walls, wall => wall.Openings.Any(opening => opening.Amount.Value is >= 1.0 and < 3.0));
    }

    /// <summary>A category the model does not have is simply not met: nothing about roofs is raised, and the walls measure as before.</summary>
    [Fact]
    public void AConfigurationWithRoofsLayeredRunsOnAModelWithoutRoofs()
    {
        CriteriaSet roofs = CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Roofs", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>()))]).Value;

        using ExportRun run = ExportPipeline.Run(InForce(Reloaded(new SavedConfiguration("roofs", roofs, null))), ModelFixture.Walls);
        using ExportRun plain = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(Lines(plain), Lines(run));
        Assert.DoesNotContain(run.Report.Warnings, warning => warning.CategoryName == "Roofs");
    }

    private static CriteriaSet Walls(double threshold, bool layered) =>
        CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride("Walls", threshold: threshold, layers: layered ? LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = QuantityUnit.CubicMetre }) : null)]).Value;

    private static SavedConfiguration Reloaded(SavedConfiguration configuration)
    {
        Result<SavedConfiguration, ConfigError> read = SavedConfigurations.Read(SavedConfigurations.Write(configuration));
        Assert.True(read.IsOk, read.IsOk ? string.Empty : read.Error.Message);
        return read.Value;
    }

    private static EffectiveCriteria InForce(SavedConfiguration configuration) => new(configuration.Criteria, ConfigSource.File, Path);

    /// <summary>Every line as written, its metrado compared bit for bit.</summary>
    private static IReadOnlyList<(string Partida, string UniqueId, string? Material, double Metrado, QuantityUnit Unit)> Lines(ExportRun run) =>
        [.. run.Result.Partidas.SelectMany(partida => partida.Lineas.Select(line => (partida.Key.PartidaCode, line.Element.UniqueId, line.Layer?.Material.MaterialName, line.Metrado.Metrado.Value, line.Metrado.Metrado.Unit)))];
}
