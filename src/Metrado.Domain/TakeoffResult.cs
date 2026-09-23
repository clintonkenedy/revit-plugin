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
        Dictionary<PartidaKey, QuantityUnit> firstUnit = [];
        List<(PartidaKey Key, QuantityUnit Unit)> encountered = [];
        List<ValidationWarning> warnings = [];

        foreach (Linea linea in Guard.RequiredValue(lineas, nameof(lineas)))
        {
            QuantityUnit unit = linea.Metrado.Metrado.Unit;
            if (!byKeyAndUnit.TryGetValue((linea.Key, unit), out List<Linea>? group))
            {
                group = [];
                byKeyAndUnit.Add((linea.Key, unit), group);
                encountered.Add((linea.Key, unit));

                if (!firstUnit.ContainsKey(linea.Key))
                {
                    firstUnit.Add(linea.Key, unit);
                }
                else
                {
                    warnings.Add(ValidationWarning.ForElement(
                        linea.Element,
                        $"Partida {linea.Key.PartidaCode} in {linea.Key.Capitulo} has lines in {firstUnit[linea.Key].Symbol()} and in {unit.Symbol()}. "
                            + "They are listed apart and not added together: no unit is converted into another."));
                }
            }

            group.Add(linea);
        }

        return new TakeoffResult([.. encountered.Select(entry => new Partida(entry.Key, byKeyAndUnit[entry]))])
        {
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Partidas whose lines came in more than one unit (<c>excel-budget-export</c>,
    /// "Unit Reported per Partida"): each unit's lines are a partida of their own,
    /// never summed with the others, and each such split is reported here.
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = [];
}
