using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c> "Empty Result Produces a Valid Workbook": "An export
/// over a model with no measurable elements SHALL still produce a valid workbook
/// containing the headers and an explicit zero-count summary", and its scenario
/// closes with "AND it states that zero measurement lines were exported".
/// <para>
/// An empty budget sheet does not make that statement. It is an absence, and the
/// specification draws the same distinction here that it draws for the unclassified
/// block: a reader must be able to tell "this run measured nothing" from "this
/// export never got that far". Only a written count says the first.
/// </para>
/// <para>
/// The count lives on the budget sheet and counts the measurement rows of that
/// sheet, because every other number this writer emits reconciles with the rows
/// beneath it. Uncoded elements were measured but were not exported as budget
/// lines, and the unclassified sheet states their count separately — so each sheet
/// carries exactly one count, and each count reconciles with its own block.
/// </para>
/// </summary>
public sealed class ExportedLineCountTests
{
    private const string BudgetSheet = "Metrado";
    private const string UnclassifiedSheet = "Unclassified";

    private const int SummaryRow = 1;
    private const int HeaderRow = 2;
    private const int CountColumn = 2;

    [Fact]
    public void TheBudgetSheetStatesHowManyMeasurementLinesItExported()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010"),
                TakeoffFixture.Line("w-2", "C1010"),
                TakeoffFixture.Line("w-3", "C2020")));

        IXLWorksheet sheet = workbook.Worksheet(BudgetSheet);

        Assert.Equal("Measurement lines exported", sheet.Cell(SummaryRow, 1).GetString());
        Assert.Equal(3, sheet.Cell(SummaryRow, CountColumn).GetDouble());
    }

    [Fact]
    public void AnExportThatMeasuredNothingStatesZeroInsteadOfLeavingTheSheetSilent()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf());

        IXLWorksheet sheet = workbook.Worksheet(BudgetSheet);

        // GetDouble throws on a blank cell rather than coercing it to 0.0, so this
        // assertion cannot be satisfied by an unwritten cell: it genuinely requires
        // the writer to have stated the zero.
        Assert.Equal(0, sheet.Cell(SummaryRow, CountColumn).GetDouble());

        // The specification asks for the headers as well as the summary, so an
        // empty run still hands the reader a workbook shaped like a budget.
        Assert.Equal(
            ["Level", "Capitulo", "Partida", "UniqueId", "Metrado", "Unit", "Boundary mode"],
            WrittenWorkbook.RowText(sheet, HeaderRow));
    }

    [Fact]
    public void UncodedElementsAreNotCountedAmongTheExportedMeasurementLines()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("w-1", "C1010"),
                TakeoffFixture.Line("w-2", "C1010"),
                TakeoffFixture.Line("uncoded-1", UnclassifiedResolver.Code),
                TakeoffFixture.Line("uncoded-2", UnclassifiedResolver.Code),
                TakeoffFixture.Line("uncoded-3", UnclassifiedResolver.Code)));

        IXLWorksheet budget = workbook.Worksheet(BudgetSheet);
        IXLWorksheet unclassified = workbook.Worksheet(UnclassifiedSheet);

        // Five elements were measured; two reached the budget. A summary reporting
        // five would name a figure no row on this sheet accounts for.
        Assert.Equal(2, budget.Cell(SummaryRow, CountColumn).GetDouble());
        Assert.Equal(3, unclassified.Cell(2, 2).GetDouble());
    }

    [Fact]
    public void TheStatedCountReconcilesWithTheMeasurementRowsBeneathIt()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                [
                    .. Enumerable
                        .Range(1, 7)
                        .Select(n => TakeoffFixture.Line($"w-{n:D2}", "C1010")),
                    .. Enumerable
                        .Range(1, 4)
                        .Select(n => TakeoffFixture.Line($"f-{n:D2}", "C2020", capitulo: "Floors")),
                ]));

        IXLWorksheet sheet = workbook.Worksheet(BudgetSheet);

        int measurementRows = WrittenWorkbook
            .BodyRows(sheet, HeaderRow)
            .Count(row => WrittenWorkbook.Text(sheet, HeaderRow, row, "Level") == "LINEA");

        Assert.Equal(11, measurementRows);
        Assert.Equal(measurementRows, (int)sheet.Cell(SummaryRow, CountColumn).GetDouble());
    }
}
