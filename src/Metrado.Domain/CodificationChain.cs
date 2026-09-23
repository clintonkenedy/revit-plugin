namespace Metrado.Domain;

/// <summary>
/// The ordered codification chain: Assembly Code, Keynote, shared parameter, rule,
/// unclassified. The first link returning a non-empty code wins and the links after
/// it are not consulted.
/// </summary>
/// <remarks>
/// The chain is just the ordered list plus this loop. A link that a given increment
/// has not built yet is simply an absent entry — there is no null-object link, no
/// registry and nothing to stub, which is what lets I2 and I4 add their resolvers
/// without touching anything already shipped.
/// <para>
/// Ending the list in <see cref="UnclassifiedResolver"/> is what makes codification
/// total, so an element nobody can code is reported rather than dropped or thrown
/// over. This type deliberately specifies nothing about how a rule link decides
/// anything: no rule syntax, no rule storage, no rule ordering beyond position in
/// this list, no filters and no user interface. Those are gated until real models
/// have produced real rules, and belong to a later change.
/// </para>
/// </remarks>
public sealed class CodificationChain
{
    private readonly IReadOnlyList<ICodeResolver> _links;

    /// <param name="links">
    /// The links in the order they are consulted. Position is the only thing that
    /// establishes priority.
    /// </param>
    public CodificationChain(IReadOnlyList<ICodeResolver> links) => _links = links;

    /// <summary>
    /// The chain in the specification's fixed order: Assembly Code, Keynote,
    /// the nominated shared parameter, unclassified. The rule link (I4) is an
    /// absent entry; so is the shared-parameter link when no parameter is
    /// nominated, so no shared value is ever read by accident.
    /// </summary>
    /// <param name="sharedParameter">The nominated shared parameter's name, or null when none is nominated.</param>
    public static CodificationChain Standard(string? sharedParameter) =>
        new(sharedParameter is null
            ? [new AssemblyCodeResolver(), new KeynoteResolver(), new UnclassifiedResolver()]
            : [new AssemblyCodeResolver(), new KeynoteResolver(), new SharedParameterResolver(sharedParameter), new UnclassifiedResolver()]);

    /// <summary>The code the first resolving link produced for the element.</summary>
    /// <remarks>
    /// Blank answers are declined here rather than trusted to each link. The Assembly
    /// Code link already trims and declines them, but the later slots are reserved for
    /// links this increment does not own — including a user-supplied rule resolver —
    /// and a blank reaching <see cref="PartidaKey"/> throws during grouping.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The chain has no terminal link, so it ran out of links with the element still
    /// uncoded. This is a misconfigured chain, not an uncodeable element: with the
    /// terminal link present no element can exhaust the chain, so the requirement's
    /// "MUST NOT throw for an uncodeable element" still holds. Substituting a terminal
    /// nobody supplied would make a wiring bug look exactly like a correct run while
    /// sending the whole model to the unclassified block.
    /// </exception>
    public string Resolve(ElementTakeoff element)
    {
        foreach (ICodeResolver link in _links)
        {
            string? code = link.Resolve(element);

            if (!string.IsNullOrWhiteSpace(code))
            {
                return code!;
            }
        }

        throw new InvalidOperationException(
            "The codification chain ran out of links without resolving a code. A chain " +
            $"must end in a link that always succeeds, such as {nameof(UnclassifiedResolver)}.");
    }
}
