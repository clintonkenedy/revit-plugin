namespace Metrado.Domain;

/// <summary>
/// The measured model arranged as the budget structure the workbook is written
/// from: capitulo, partida, linea de medicion.
/// </summary>
public sealed record TakeoffResult(IReadOnlyList<Partida> Partidas)
{
    /// <summary>How many lineas de medicion this result holds.</summary>
    /// <remarks>
    /// Counted across the partidas rather than stored, so it cannot drift from the
    /// lines it counts. This is the figure the "empty measurement set" requirement
    /// is stated in: a result with zero lines measured nothing, whatever the model
    /// happened to contain.
    /// </remarks>
    public int LineCount => Partidas.Sum(partida => partida.Lineas.Count);

    /// <summary>
    /// Groups measured lines into partidas, keyed by capitulo plus resolved code,
    /// and apart by unit when one key's lines disagree on it.
    /// </summary>
    /// <remarks>
    /// Element type identity is deliberately not part of the key: two distinct
    /// types that resolved to the same code are one partida holding the instances
    /// of both, which is the collapse <c>partida-codification</c> requires.
    /// <para>
    /// Partidas come back in the order their first line was encountered. That is a
    /// deliberate list rather than a dictionary enumeration, so the order is a
    /// property of the input rather than of a hash bucket. Sorting for the workbook
    /// belongs to the writer, which owns deterministic row ordering.
    /// </para>
    /// </remarks>
    public static TakeoffResult Group(IReadOnlyList<Linea> lineas)
    {
        Dictionary<(PartidaKey Key, QuantityUnit Unit), List<Linea>> byKeyAndUnit = [];
        List<(PartidaKey Key, QuantityUnit Unit)> encountered = [];

        foreach (Linea linea in Guard.RequiredValue(lineas, nameof(lineas)))
        {
            (PartidaKey, QuantityUnit) entry = (linea.Key, linea.Metrado.Metrado.Unit);
            if (!byKeyAndUnit.TryGetValue(entry, out List<Linea>? group))
            {
                group = [];
                byKeyAndUnit.Add(entry, group);
                encountered.Add(entry);
            }

            group.Add(linea);
        }

        return new TakeoffResult([.. encountered.Select(entry => new Partida(entry.Key, byKeyAndUnit[entry]))])
        {
            Warnings = SplitWarnings(byKeyAndUnit),
        };
    }

    /// <summary>
    /// One warning per extra unit of a split key. Revit's iteration order
    /// decides nothing: the units are named in their declared order, and each
    /// warning names the first element, by UniqueId, of the unit it reports.
    /// </summary>
    private static List<ValidationWarning> SplitWarnings(Dictionary<(PartidaKey Key, QuantityUnit Unit), List<Linea>> byKeyAndUnit) =>
    [
        .. byKeyAndUnit.Keys
            .GroupBy(entry => entry.Key)
            .Where(key => key.Count() > 1)
            .OrderBy(key => key.Key.Capitulo, StringComparer.Ordinal)
            .ThenBy(key => key.Key.PartidaCode, StringComparer.Ordinal)
            .SelectMany(key =>
            {
                QuantityUnit[] units = [.. key.Select(entry => entry.Unit).OrderBy(unit => unit)];
                return units.Skip(1).Select(unit => ValidationWarning.ForElement(
                    byKeyAndUnit[(key.Key, unit)].OrderBy(linea => linea.Element.UniqueId, StringComparer.Ordinal).First().Element,
                    Describe(key.Key, units[0], unit)));
            }),
    ];

    private static string Describe(PartidaKey key, QuantityUnit first, QuantityUnit other) =>
        key.PartidaCode == UnclassifiedResolver.Code
            ? $"Unclassified lines in {key.Capitulo} come in {first.Symbol()} and in {other.Symbol()}. They are not added together: no unit is converted into another."
            : $"Partida {key.PartidaCode} in {key.Capitulo} has lines in {first.Symbol()} and in {other.Symbol()}. "
                + "They are listed apart and not added together: no unit is converted into another.";

    /// <summary>
    /// Partidas whose lines came in more than one unit (<c>excel-budget-export</c>,
    /// "Unit Reported per Partida"): each unit's lines are a partida of their own,
    /// never summed with the others, and each such split is reported here.
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = [];
}
