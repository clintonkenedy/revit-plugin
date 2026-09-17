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
    /// Groups measured lines into partidas, keyed by capitulo plus resolved code
    /// and by nothing else.
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
        Dictionary<PartidaKey, List<Linea>> byKey = [];
        List<PartidaKey> encountered = [];

        foreach (Linea linea in Guard.RequiredValue(lineas, nameof(lineas)))
        {
            if (!byKey.TryGetValue(linea.Key, out List<Linea>? group))
            {
                group = [];
                byKey.Add(linea.Key, group);
                encountered.Add(linea.Key);
            }

            group.Add(linea);
        }

        return new TakeoffResult([.. encountered.Select(key => new Partida(key, byKey[key]))]);
    }
}
