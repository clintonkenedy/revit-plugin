using ClosedXML.Excel;
using Metrado.Domain;

namespace Metrado.Excel;

/// <summary>
/// Writes a measured model out as the budget workbook: capitulo, partida and
/// linea de medicion.
/// </summary>
public static class TakeoffWorkbook
{
    /// <summary>The sheet holding the coded budget.</summary>
    public const string BudgetSheetName = "Metrado";

    private const int HeaderRow = 1;

    private const int LevelColumn = 1;
    private const int CapituloColumn = 2;
    private const int PartidaColumn = 3;
    private const int UniqueIdColumn = 4;
    private const int MetradoColumn = 5;

    private const string CapituloLevel = "CAPITULO";
    private const string PartidaLevel = "PARTIDA";
    private const string LineaLevel = "LINEA";

    /// <summary>Writes <paramref name="result"/> into <paramref name="destination"/>.</summary>
    /// <remarks>
    /// The stream is written but not closed, so a caller that owns it can keep
    /// using it — the tests read the produced bytes straight back.
    /// </remarks>
    public static void Write(TakeoffResult result, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(destination);

        using XLWorkbook workbook = new();

        WriteBudget(workbook.Worksheets.Add(BudgetSheetName), result);

        workbook.SaveAs(destination);
    }

    private static void WriteBudget(IXLWorksheet sheet, TakeoffResult result)
    {
        sheet.Cell(HeaderRow, LevelColumn).Value = "Level";
        sheet.Cell(HeaderRow, CapituloColumn).Value = "Capitulo";
        sheet.Cell(HeaderRow, PartidaColumn).Value = "Partida";
        sheet.Cell(HeaderRow, UniqueIdColumn).Value = "UniqueId";
        sheet.Cell(HeaderRow, MetradoColumn).Value = "Metrado";

        int row = HeaderRow + 1;

        foreach (IGrouping<string, Partida> capitulo in ByCapitulo(result))
        {
            sheet.Cell(row, LevelColumn).Value = CapituloLevel;
            sheet.Cell(row, CapituloColumn).Value = capitulo.Key;
            sheet.Cell(row, MetradoColumn).Value = CapituloTotal(capitulo);
            row++;

            foreach (Partida partida in capitulo)
            {
                sheet.Cell(row, LevelColumn).Value = PartidaLevel;
                sheet.Cell(row, CapituloColumn).Value = capitulo.Key;
                sheet.Cell(row, PartidaColumn).Value = partida.Key.PartidaCode;
                sheet.Cell(row, MetradoColumn).Value = partida.Total.Value;
                row++;

                foreach (Linea linea in InOrder(partida))
                {
                    sheet.Cell(row, LevelColumn).Value = LineaLevel;
                    sheet.Cell(row, CapituloColumn).Value = capitulo.Key;
                    sheet.Cell(row, PartidaColumn).Value = partida.Key.PartidaCode;
                    sheet.Cell(row, UniqueIdColumn).Value = linea.Element.UniqueId;
                    sheet.Cell(row, MetradoColumn).Value = linea.Metrado.Metrado.Value;
                    row++;
                }
            }
        }
    }

    /// <summary>
    /// The partidas arranged into capitulos, both in the order the workbook must
    /// present them.
    /// </summary>
    /// <remarks>
    /// Sorting before grouping is what puts the capitulos in order too: grouping
    /// keeps the order each key first appears in, so an already-sorted sequence
    /// yields sorted groups.
    /// <para>
    /// Every comparison is ordinal. The default string comparer is culture-aware,
    /// which would make the row order of a budget a property of the machine that
    /// exported it — and the requirement is that identical input produces identical
    /// ordering, not ordering identical to the exporter's locale.
    /// </para>
    /// </remarks>
    private static IEnumerable<IGrouping<string, Partida>> ByCapitulo(TakeoffResult result) =>
        result
            .Partidas
            .OrderBy(partida => partida.Key.Capitulo, StringComparer.Ordinal)
            .ThenBy(partida => partida.Key.PartidaCode, StringComparer.Ordinal)
            .GroupBy(partida => partida.Key.Capitulo, StringComparer.Ordinal);

    /// <summary>The measurement lines of a partida, in the order they are written.</summary>
    /// <remarks>
    /// <c>UniqueId</c> is the stable per-line key: it is unique per element and
    /// non-empty by construction, so in I1 — where one element produces one line —
    /// it orders the lines totally rather than merely consistently. The I3 material
    /// layers that put several lines under one host <c>UniqueId</c> will need a
    /// second key beside it, because ties here fall back to arrival order and
    /// arrival order is exactly what must not decide anything.
    /// </remarks>
    private static IEnumerable<Linea> InOrder(Partida partida) =>
        partida.Lineas.OrderBy(linea => linea.Element.UniqueId, StringComparer.Ordinal);

    /// <summary>The sum of a capitulo's partida totals.</summary>
    /// <remarks>
    /// Written at full double precision rather than rounded, so the subtotal a
    /// reviewer checks reconciles with the lines above it to floating-point
    /// accuracy and the "stated rounding tolerance" the specification allows is
    /// never spent by the writer.
    /// <para>
    /// The partida totals themselves come from <see cref="Partida.Total"/>, which
    /// already refuses to add lines measured in different units. Across partidas of
    /// one capitulo the same guarantee holds structurally in I1 — one capitulo is
    /// one criterion, so one unit — and the per-partida unit that would make it
    /// checkable in the workbook arrives with the I2 unit column.
    /// </para>
    /// </remarks>
    private static double CapituloTotal(IEnumerable<Partida> partidas) =>
        partidas.Sum(partida => partida.Total.Value);
}
