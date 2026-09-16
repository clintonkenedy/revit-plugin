namespace Metrado.Domain;

/// <summary>
/// The identity of a partida: a capitulo plus the code the codification chain
/// resolved, and nothing else.
/// </summary>
/// <remarks>
/// Two distinct wall types that resolve to the same code therefore produce one
/// partida holding every contributing instance as its own linea. Type identity is
/// carried separately on the element and is deliberately not part of this key.
/// <para>
/// Both components are validated on construction <em>and</em> on the <c>with</c>
/// copy path, because a blank code would surface in the budget as an unnamed
/// partida.
/// </para>
/// </remarks>
public readonly record struct PartidaKey
{
    private readonly string _capitulo;
    private readonly string _partidaCode;

    public PartidaKey(string capitulo, string partidaCode)
    {
        _capitulo = Guard.RequiredText(capitulo, nameof(capitulo));
        _partidaCode = Guard.RequiredText(partidaCode, nameof(partidaCode));
    }

    public string Capitulo
    {
        get => _capitulo;
        init => _capitulo = Guard.RequiredText(value, nameof(Capitulo));
    }

    public string PartidaCode
    {
        get => _partidaCode;
        init => _partidaCode = Guard.RequiredText(value, nameof(PartidaCode));
    }
}
