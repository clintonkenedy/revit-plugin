namespace Metrado.Domain;

/// <summary>
/// The Keynote link, second in the chain (<c>partida-codification</c>, "Keynote
/// Resolver"): what the adapter read off the element's Keynote parameter, by
/// its built-in identifier, under the Assembly Code link's rule. The reading
/// is trimmed, then judged: empty or whitespace is no code, and passes control
/// to the next link. Revit-free and side-effect free, like every link.
/// </summary>
public sealed class KeynoteResolver : ICodeResolver
{
    public string? Resolve(ElementTakeoff element)
    {
        string? trimmed = element.Codes.Keynote?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
