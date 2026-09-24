using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// <c>model-validation-warnings</c>, "Warnings Do Not Block the Export" (task
/// 3.4): a run whose validation pass raises fifteen warnings still writes the
/// workbook, and the full list, not only its count, is in the run report.
/// Only a configuration error stops a run, and it does so before any element
/// is read, in criteria resolution.
/// </summary>
public sealed class WarningsDoNotBlockTheExportTests
{
    [Fact]
    public void FifteenWarningsStillWriteTheWorkbook()
    {
        List<ElementTakeoff> model =
        [
            .. Enumerable.Range(1, 15).Select(index => Wall($"suspect-{index:00}", squareMetres: 0.0)),
            Wall("clean-1", squareMetres: 18.0),
            Wall("clean-2", squareMetres: 12.0),
        ];

        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(model);

        Assert.Equal(17, run.Budget.StatedExportedLineCount());
        Assert.Equal(model.Select(wall => wall.UniqueId).Order(StringComparer.Ordinal), run.Budget.ExportedIdentifiers().Order(StringComparer.Ordinal));
        Assert.Equal(18.0, run.Budget.MetradoOf("clean-1"), 9);
        Assert.Equal(15, run.Report.WarningCount);
    }

    /// <summary>The report carries each warning, naming its element and condition, not a number standing in for them.</summary>
    [Fact]
    public void TheRunReportCarriesEveryWarningNotACount()
    {
        List<ElementTakeoff> model = [.. Enumerable.Range(1, 15).Select(index => Wall($"suspect-{index:00}", squareMetres: 0.0))];

        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(model);

        Assert.Equal(model.Select(wall => wall.UniqueId), run.Report.Warnings.Select(warning => warning.UniqueId));
        Assert.All(run.Report.Warnings, warning => Assert.StartsWith("Its metrado is 0 m2", warning.Condition, StringComparison.Ordinal));
    }

    private static ElementTakeoff Wall(string uniqueId, double squareMetres) =>
        new(
            UniqueId: uniqueId,
            CategoryName: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeKey: "type",
            Codes: new CodificationReadings("B2010", keynote: null, sharedParameters: new Dictionary<string, string?>()),
            Quantities: [new RawQuantity("HOST_AREA_COMPUTED", new Quantity(squareMetres, QuantityUnit.SquareMetre))],
            Openings: []);
}
