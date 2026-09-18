using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel.Tests;

/// <summary>
/// Reads a workbook the writer actually produced.
/// </summary>
/// <remarks>
/// Every assertion goes through a serialise-then-reopen round trip rather than
/// inspecting the writer's own state. A writer that arranges rows correctly in
/// memory and fails to persist them ships an empty budget, and only reading the
/// bytes back can tell the two apart.
/// </remarks>
internal static class WrittenWorkbook
{
    /// <summary>Writes <paramref name="result"/> and reopens the produced bytes.</summary>
    internal static XLWorkbook Of(TakeoffResult result)
    {
        MemoryStream stream = new();
        TakeoffWorkbook.Write(result, stream);
        stream.Position = 0;

        return new XLWorkbook(stream);
    }

    /// <summary>The text of every populated cell in <paramref name="row"/>.</summary>
    internal static IReadOnlyList<string> RowText(IXLWorksheet sheet, int row) =>
        [.. sheet.Row(row).CellsUsed().Select(cell => cell.GetString())];

    /// <summary>
    /// Every used cell of the sheet, one string per row, blanks included.
    /// </summary>
    /// <remarks>
    /// Whole rows rather than selected columns, so a comparison between two
    /// workbooks cannot pass by agreeing on the columns the test happened to look
    /// at while disagreeing everywhere else.
    /// </remarks>
    internal static IReadOnlyList<string> Grid(IXLWorksheet sheet)
    {
        IXLRow? lastRow = sheet.LastRowUsed();
        IXLColumn? lastColumn = sheet.LastColumnUsed();

        if (lastRow is null || lastColumn is null)
        {
            return [];
        }

        return
        [
            .. Enumerable
                .Range(1, lastRow.RowNumber())
                .Select(row => string.Join(
                    " | ",
                    Enumerable
                        .Range(1, lastColumn.ColumnNumber())
                        .Select(column => sheet.Cell(row, column).GetString()))),
        ];
    }

    /// <summary>
    /// The rows below <paramref name="headerRow"/> that carry content.
    /// </summary>
    internal static IReadOnlyList<int> BodyRows(IXLWorksheet sheet, int headerRow)
    {
        IXLRow? last = sheet.LastRowUsed();

        return last is null || last.RowNumber() <= headerRow
            ? []
            : [.. Enumerable.Range(headerRow + 1, last.RowNumber() - headerRow)];
    }

    /// <summary>
    /// Locates a column by the header it is written under, so the assertions read
    /// the sheet the way a reviewer does rather than by a position they would have
    /// to count.
    /// </summary>
    internal static int ColumnOf(IXLWorksheet sheet, int headerRow, string header)
    {
        IXLCell? cell = sheet
            .Row(headerRow)
            .CellsUsed()
            .FirstOrDefault(candidate => candidate.GetString() == header);

        Assert.True(
            cell is not null,
            $"Sheet '{sheet.Name}' has no '{header}' column. Headers found: "
                + string.Join(", ", RowText(sheet, headerRow)));

        return cell!.Address.ColumnNumber;
    }

    /// <summary>The text in one cell, located by its column header.</summary>
    internal static string Text(IXLWorksheet sheet, int headerRow, int row, string header) =>
        sheet.Cell(row, ColumnOf(sheet, headerRow, header)).GetString();

    /// <summary>
    /// The number in one cell, located by its column header.
    /// </summary>
    /// <remarks>
    /// Read as a double rather than as text on purpose: a numeric cell renders
    /// through the current culture, so a string comparison would pass or fail on
    /// this host's decimal separator instead of on the value the writer stored.
    /// </remarks>
    internal static double Number(IXLWorksheet sheet, int headerRow, int row, string header) =>
        sheet.Cell(row, ColumnOf(sheet, headerRow, header)).GetDouble();
}
