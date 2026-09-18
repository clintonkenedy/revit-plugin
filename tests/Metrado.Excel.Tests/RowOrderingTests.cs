using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// <c>excel-budget-export</c> "Deterministic Output Ordering": "For identical
/// input the writer SHALL produce identical row ordering. Rows MUST be ordered by
/// capitulo, then partida code, then a stable per-line key. Ordering MUST NOT
/// depend on Revit's element iteration order."
/// <para>
/// The last sentence is the one that needs a test rather than a sort call. Domain
/// hands the writer partidas in the order their first line was encountered, and
/// that order is Revit's. Sorting code that is only ever fed already-sorted input
/// proves nothing, so every case here arrives in an order that is deliberately not
/// the order it must come out in, and one of them feeds the same lines twice in
/// two different arrival orders.
/// </para>
/// </summary>
public sealed class RowOrderingTests
{
    private const string SheetName = "Metrado";
    private const int HeaderRow = 1;

    /// <summary>
    /// The same five lines, arriving in two unrelated orders. Neither sequence is
    /// the order the workbook must present them in.
    /// </summary>
    private static Linea[] OneArrival() =>
        [
            TakeoffFixture.Line("w-9", "C2020", metrado: 9.0),
            TakeoffFixture.Line("f-2", "C1010", metrado: 2.0, capitulo: "Floors"),
            TakeoffFixture.Line("w-3", "C1010", metrado: 3.0),
            TakeoffFixture.Line("w-1", "C1010", metrado: 1.0),
            TakeoffFixture.Line("f-1", "C1010", metrado: 5.0, capitulo: "Floors"),
        ];

    private static Linea[] AnotherArrival() =>
        [
            TakeoffFixture.Line("f-1", "C1010", metrado: 5.0, capitulo: "Floors"),
            TakeoffFixture.Line("w-1", "C1010", metrado: 1.0),
            TakeoffFixture.Line("w-9", "C2020", metrado: 9.0),
            TakeoffFixture.Line("f-2", "C1010", metrado: 2.0, capitulo: "Floors"),
            TakeoffFixture.Line("w-3", "C1010", metrado: 3.0),
        ];

    [Fact]
    public void RowsComeOutOrderedByCapituloThenPartidaCodeThenLineKey()
    {
        using XLWorkbook workbook = WrittenWorkbook.Of(TakeoffFixture.ResultOf(OneArrival()));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(
            [
                ("CAPITULO", "Floors", string.Empty, string.Empty),
                ("PARTIDA", "Floors", "C1010", string.Empty),
                ("LINEA", "Floors", "C1010", "f-1"),
                ("LINEA", "Floors", "C1010", "f-2"),
                ("CAPITULO", "Walls", string.Empty, string.Empty),
                ("PARTIDA", "Walls", "C1010", string.Empty),
                ("LINEA", "Walls", "C1010", "w-1"),
                ("LINEA", "Walls", "C1010", "w-3"),
                ("PARTIDA", "Walls", "C2020", string.Empty),
                ("LINEA", "Walls", "C2020", "w-9"),
            ],
            Placements(sheet));
    }

    [Fact]
    public void TheSameLinesArrivingInADifferentOrderProduceTheSameWorkbookRows()
    {
        using XLWorkbook first = WrittenWorkbook.Of(TakeoffFixture.ResultOf(OneArrival()));
        using XLWorkbook second = WrittenWorkbook.Of(TakeoffFixture.ResultOf(AnotherArrival()));

        IReadOnlyList<string> rows = WrittenWorkbook.Grid(first.Worksheet(SheetName));

        // Guards the comparison itself: two empty sheets are also identical, and
        // that agreement would prove nothing about ordering.
        Assert.Equal(11, rows.Count);
        Assert.Equal(rows, WrittenWorkbook.Grid(second.Worksheet(SheetName)));
    }

    [Fact]
    public void TheOrderIsTheWritersOwnRatherThanTheOrderPartidasWereGroupedIn()
    {
        TakeoffResult result = TakeoffFixture.ResultOf(OneArrival());

        // Domain hands them over in first-encounter order, which is Revit's. If the
        // workbook simply echoed that, this test would be asserting nothing.
        Assert.Equal(
            ["C2020", "C1010", "C1010"],
            [.. result.Partidas.Select(partida => partida.Key.PartidaCode)]);

        using XLWorkbook workbook = WrittenWorkbook.Of(result);

        Assert.Equal(
            ["C1010", "C1010", "C2020"],
            [.. PartidaRows(workbook.Worksheet(SheetName))]);
    }

    [Fact]
    public void LineKeysAreComparedOrdinallySoTheOrderIsNotTheExportingHostsLocale()
    {
        // A Revit UniqueId is a GUID whose hex digits may arrive in either case.
        // Ordinal comparison orders by code point, so every uppercase identifier
        // precedes every lowercase one; a culture-aware comparison folds the two
        // together and puts "a-wall" first. Identical input has to produce
        // identical ordering everywhere, not ordering that agrees with whichever
        // locale ran the export.
        using XLWorkbook workbook = WrittenWorkbook.Of(
            TakeoffFixture.ResultOf(
                TakeoffFixture.Line("a-wall", "C1010"),
                TakeoffFixture.Line("B-wall", "C1010")));

        IXLWorksheet sheet = workbook.Worksheet(SheetName);

        Assert.Equal(
            ["B-wall", "a-wall"],
            [
                .. WrittenWorkbook
                    .BodyRows(sheet, HeaderRow)
                    .Where(row => Text(sheet, row, "Level") == "LINEA")
                    .Select(row => Text(sheet, row, "UniqueId")),
            ]);
    }

    private static IReadOnlyList<(string, string, string, string)> Placements(IXLWorksheet sheet) =>
        [
            .. WrittenWorkbook
                .BodyRows(sheet, HeaderRow)
                .Select(row => (
                    Text(sheet, row, "Level"),
                    Text(sheet, row, "Capitulo"),
                    Text(sheet, row, "Partida"),
                    Text(sheet, row, "UniqueId"))),
        ];

    private static IEnumerable<string> PartidaRows(IXLWorksheet sheet) =>
        WrittenWorkbook
            .BodyRows(sheet, HeaderRow)
            .Where(row => Text(sheet, row, "Level") == "PARTIDA")
            .Select(row => Text(sheet, row, "Partida"));

    private static string Text(IXLWorksheet sheet, int row, string header) =>
        WrittenWorkbook.Text(sheet, HeaderRow, row, header);
}
