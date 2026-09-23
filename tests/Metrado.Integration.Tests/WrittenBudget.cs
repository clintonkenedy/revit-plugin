using ClosedXML.Excel;
using Metrado.Excel;

namespace Metrado.Integration.Tests;

/// <summary>
/// The workbook as a reviewer reads it: by the headers printed on the sheet.
/// </summary>
/// <remarks>
/// Every lookup goes through the column <em>header</em>, never a column number. A
/// reviewer opening the file finds the metrado under "Metrado"; a test that counted
/// to the fifth column would keep passing if the writer reordered its columns, and
/// would fail for a reason that has nothing to do with the budget.
/// <para>
/// The row positions below are the exception, because a row's position is part of
/// the layout rather than something the sheet labels. They are stated once each.
/// </para>
/// </remarks>
internal sealed class WrittenBudget
{
    /// <summary>Row 1 holds the exported-line summary, so the headers start at row 2.</summary>
    private const int HeaderRow = 2;

    private const int SummaryRow = 1;
    private const int SummaryCountColumn = 2;

    /// <summary>The unclassified sheet's headers sit below its label and its count.</summary>
    private const int UnclassifiedHeaderRow = 3;

    private const string LevelHeader = "Level";
    private const string PartidaHeader = "Partida";
    private const string UniqueIdHeader = "UniqueId";
    private const string MetradoHeader = "Metrado";
    private const string BoundaryModeHeader = "Boundary mode";
    private const string UnitHeader = "Unit";
    private const string MaterialHeader = "Material";
    private const string LayersHeader = "Layers";

    private const string PartidaLevel = "PARTIDA";
    private const string LineaLevel = "LINEA";

    private readonly XLWorkbook _workbook;

    internal WrittenBudget(XLWorkbook workbook) => _workbook = workbook;

    /// <summary>The metrado written on one wall's measurement line.</summary>
    internal double MetradoOf(string uniqueId) =>
        Sheet.Cell(LineaRow(uniqueId), ColumnOf(Sheet, HeaderRow, MetradoHeader)).GetDouble();

    /// <summary>The boundary convention written on one wall's measurement line.</summary>
    internal string BoundaryModeOf(string uniqueId) =>
        Sheet.Cell(LineaRow(uniqueId), ColumnOf(Sheet, HeaderRow, BoundaryModeHeader)).GetString();

    /// <summary>The subtotal written on one partida's row.</summary>
    internal double SubtotalOf(string partidaCode) =>
        Sheet.Cell(PartidaRow(partidaCode), ColumnOf(Sheet, HeaderRow, MetradoHeader)).GetDouble();

    /// <summary>
    /// How many measurement lines the budget sheet states it exported.
    /// </summary>
    /// <remarks>
    /// Read as a number, not as text: a blank cell throws here rather than reading
    /// as an empty string, which is what keeps "the sheet states zero" distinct from
    /// "the sheet says nothing".
    /// </remarks>
    internal double StatedExportedLineCount() =>
        Sheet.Cell(SummaryRow, SummaryCountColumn).GetDouble();

    /// <summary>The identifiers on the budget sheet's measurement lines, in written order.</summary>
    internal IReadOnlyList<string> ExportedIdentifiers()
    {
        int level = ColumnOf(Sheet, HeaderRow, LevelHeader);
        int identifier = ColumnOf(Sheet, HeaderRow, UniqueIdHeader);

        return
        [
            .. BodyRows(Sheet, HeaderRow)
                .Where(row => Sheet.Cell(row, level).GetString() == LineaLevel)
                .Select(row => Sheet.Cell(row, identifier).GetString()),
        ];
    }

    /// <summary>The measurement lines that name a material, in written order: partida, identifier, material, layers, metrado and unit.</summary>
    internal IReadOnlyList<(string Partida, string UniqueId, string Material, string Layers, double Metrado, string Unit)> LayerLines()
    {
        int level = ColumnOf(Sheet, HeaderRow, LevelHeader);
        int material = ColumnOf(Sheet, HeaderRow, MaterialHeader);

        return
        [
            .. BodyRows(Sheet, HeaderRow)
                .Where(row => Sheet.Cell(row, level).GetString() == LineaLevel && Sheet.Cell(row, material).GetString().Length > 0)
                .Select(row => (
                    Sheet.Cell(row, ColumnOf(Sheet, HeaderRow, PartidaHeader)).GetString(),
                    Sheet.Cell(row, ColumnOf(Sheet, HeaderRow, UniqueIdHeader)).GetString(),
                    Sheet.Cell(row, material).GetString(),
                    Sheet.Cell(row, ColumnOf(Sheet, HeaderRow, LayersHeader)).GetString(),
                    Math.Round(Sheet.Cell(row, ColumnOf(Sheet, HeaderRow, MetradoHeader)).GetDouble(), 9),
                    Sheet.Cell(row, ColumnOf(Sheet, HeaderRow, UnitHeader)).GetString())),
        ];
    }

    /// <summary>The identifiers listed on the unclassified sheet, in written order.</summary>
    internal IReadOnlyList<string> UnclassifiedIdentifiers()
    {
        IXLWorksheet sheet = _workbook.Worksheet(TakeoffWorkbook.UnclassifiedSheetName);
        int column = ColumnOf(sheet, UnclassifiedHeaderRow, UniqueIdHeader);

        return
        [
            .. BodyRows(sheet, UnclassifiedHeaderRow)
                .Select(row => sheet.Cell(row, column).GetString()),
        ];
    }

    private IXLWorksheet Sheet => _workbook.Worksheet(TakeoffWorkbook.BudgetSheetName);

    private int LineaRow(string uniqueId)
    {
        int level = ColumnOf(Sheet, HeaderRow, LevelHeader);
        int identifier = ColumnOf(Sheet, HeaderRow, UniqueIdHeader);

        return SingleRow(
            BodyRows(Sheet, HeaderRow).Where(row =>
                Sheet.Cell(row, level).GetString() == LineaLevel
                && Sheet.Cell(row, identifier).GetString() == uniqueId),
            $"a {LineaLevel} row for '{uniqueId}'");
    }

    private int PartidaRow(string partidaCode)
    {
        int level = ColumnOf(Sheet, HeaderRow, LevelHeader);
        int partida = ColumnOf(Sheet, HeaderRow, PartidaHeader);

        return SingleRow(
            BodyRows(Sheet, HeaderRow).Where(row =>
                Sheet.Cell(row, level).GetString() == PartidaLevel
                && Sheet.Cell(row, partida).GetString() == partidaCode),
            $"a {PartidaLevel} row for '{partidaCode}'");
    }

    private static IEnumerable<int> BodyRows(IXLWorksheet sheet, int headerRow)
    {
        IXLRow? last = sheet.LastRowUsed();

        return last is null || last.RowNumber() <= headerRow
            ? []
            : Enumerable.Range(headerRow + 1, last.RowNumber() - headerRow);
    }

    /// <summary>
    /// The one row matching, refusing both no match and several.
    /// </summary>
    /// <remarks>
    /// Several matches is checked rather than assumed away. In I1 one element
    /// produces one line, so a duplicate row is a real defect — and taking the first
    /// would hide it behind an assertion that still passed.
    /// </remarks>
    private static int SingleRow(IEnumerable<int> rows, string description)
    {
        int[] matched = [.. rows];

        Assert.True(
            matched.Length == 1,
            $"Expected exactly one budget row that is {description}, found {matched.Length}.");

        return matched[0];
    }

    private static int ColumnOf(IXLWorksheet sheet, int headerRow, string header)
    {
        IXLCell? cell = sheet
            .Row(headerRow)
            .CellsUsed()
            .FirstOrDefault(candidate => candidate.GetString() == header);

        Assert.True(
            cell is not null,
            $"Sheet '{sheet.Name}' has no '{header}' column on row {headerRow}. Headers found: "
                + string.Join(
                    ", ",
                    sheet.Row(headerRow).CellsUsed().Select(used => used.GetString())));

        return cell!.Address.ColumnNumber;
    }
}
