using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>metrado-measurement</c> "Openings Threshold Correction": "The active mode
/// MUST be recorded on the measurement result and surfaced in the exported
/// workbook, so a reviewer can tell which convention produced the numbers. A
/// configurable rule whose convention is not reported produces two different
/// defensible budgets from one model with no way to distinguish them."
/// <para>
/// The mode is written on the measurement line rather than on the capitulo or in a
/// corner of the sheet, because the line is where the fact already lives:
/// <c>MetradoResult.AppliedMode</c> records what was applied to that element. Any
/// second copy of it elsewhere in the workbook would be a second home for one
/// fact, and the two only ever diverge on the runs worth reporting.
/// </para>
/// </summary>
public sealed class BoundaryModeTests
{
    private const string SheetName = "Metrado";
    private const int HeaderRow = 2;

    [Fact]
    public void EveryMeasurementLineRecordsTheConventionThatProducedItsMetrado()
    {
        // Both modes in one workbook, so the column cannot be right by writing one
        // constant: the whole point of reporting the mode is that it can differ.
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010", appliedMode: BoundaryMode.Exclusive),
                TakeoffFixture.Line("w-2", "C2020", appliedMode: BoundaryMode.Inclusive)));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(
            [("w-1", "exclusive"), ("w-2", "inclusive")],
            [
                .. WrittenWorkbook
                    .BodyRows(sheet, HeaderRow)
                    .Where(row => Text(sheet, row, "Level") == "LINEA")
                    .Select(row => (Text(sheet, row, "UniqueId"), Text(sheet, row, "Boundary mode"))),
            ]);
    }

    [Fact]
    public void TheModeIsSpeltTheWayTheSpecificationAndTheCriteriaFileSpellIt()
    {
        // A reviewer compares the workbook against the criteria that produced it,
        // and the criteria name the modes `exclusive` and `inclusive`. Rendering
        // the enum member instead would hand them two spellings of one convention.
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010", appliedMode: BoundaryMode.Exclusive)));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal("exclusive", Text(sheet, MeasurementRow(sheet), "Boundary mode"));
        Assert.NotEqual(
            BoundaryMode.Exclusive.ToString(),
            Text(sheet, MeasurementRow(sheet), "Boundary mode"));
    }

    [Fact]
    public void AModeThatNamesNoConventionStopsTheExportRatherThanReportingAGuess()
    {
        TakeoffResult result = TakeoffFixture.ResultOf(
            TakeoffFixture.Line("w-1", "C1010", appliedMode: (BoundaryMode)0));

        using MemoryStream destination = new();

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => TakeoffWorkbook.Write(result, destination));

        Assert.Contains("boundary mode", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GroupingRowsCarryNoModeBecauseNoSingleMeasurementProducedThem()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(TakeoffFixture.Line("w-1", "C1010")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.All(
            WrittenWorkbook.BodyRows(sheet, HeaderRow).Where(row =>
                Text(sheet, row, "Level") != "LINEA"),
            row => Assert.Equal(string.Empty, Text(sheet, row, "Boundary mode")));
    }

    private static int MeasurementRow(IXLWorksheet sheet) =>
        WrittenWorkbook
            .BodyRows(sheet, HeaderRow)
            .Single(row => Text(sheet, row, "Level") == "LINEA");

    private static string Text(IXLWorksheet sheet, int row, string header) =>
        WrittenWorkbook.Text(sheet, HeaderRow, row, header);
}
