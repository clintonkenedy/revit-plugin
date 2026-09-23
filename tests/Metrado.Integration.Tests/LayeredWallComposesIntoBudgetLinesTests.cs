using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// Tasks 3.1, 3.2 and 3.5 composed: a criteria file that takes walls off by
/// material layer, resolved; a layered wall measured by its materials, each
/// line coded by its material; and the budget sheet read back from the bytes.
/// </summary>
/// <remarks>
/// The wall is the domain suite's: 12.51 m2 of 10 mm tile, 130 mm brick and
/// 15 mm plaster, with a 0.60 m2 window the 1 m2 threshold adds back and a
/// 1.89 m2 door it keeps deducted. The file puts Structure in m3, so the brick
/// line keeps Revit's deduction and the other two get the window back.
/// </remarks>
public sealed class LayeredWallComposesIntoBudgetLinesTests
{
    private const string CriteriaText = """{ "Walls": { "layers": { "Structure": "m3" } } }""";

    [Fact]
    public void AThreeMaterialWallIsThreeBudgetLinesUnderOneUniqueId()
    {
        using ExportRun run = ExportPipeline.Run(Resolved(), [ModelFixture.LayeredWall("w-layered", wholeVolumeOver: 0)]);

        Assert.Equal(
            [
                ("02.01.01", "w-layered", "Ladrillo", "Structure 130 mm", 1.6263, "m3"),
                ("02.04.01", "w-layered", "Tarrajeo", "Finish2 15 mm", 13.11, "m2"),
                ("02.05.01", "w-layered", "Enchape", "Finish1 10 mm", 13.11, "m2"),
            ],
            run.Budget.LayerLines());
        Assert.Empty(run.Budget.UnclassifiedIdentifiers());
        Assert.Contains(run.Report.Warnings, warning => warning.Condition.EndsWith("(Ladrillo 0.078 m3).", StringComparison.Ordinal));
    }

    /// <summary>A wall whose materials do not add up is one whole line under its own code, and the report says why.</summary>
    [Fact]
    public void AWallWhoseMaterialsDoNotAddUpIsOneWholeLine()
    {
        using ExportRun run = ExportPipeline.Run(Resolved(), [ModelFixture.LayeredWall("w-whole", wholeVolumeOver: 0.01)]);

        Assert.Empty(run.Budget.LayerLines());
        Assert.Equal(["w-whole"], run.Budget.ExportedIdentifiers());
        Assert.Equal(13.11, run.Budget.MetradoOf("w-whole"), 9);
        Assert.Contains(run.Report.Warnings, warning => warning.Condition.StartsWith("Not taken off by material layer, so measured whole", StringComparison.Ordinal));
    }

    private static EffectiveCriteria Resolved()
    {
        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(CriteriaFileLookup.Found(CriteriaText), "C:/model/metrado.criteria.json");
        Assert.True(resolved.IsOk, resolved.IsOk ? string.Empty : resolved.Error.Message);
        return resolved.Value;
    }
}
