namespace Metrado.Domain;

/// <summary>
/// The shared-parameter link, third in the chain (<c>partida-codification</c>,
/// "Shared Parameter Resolver"): the value of the one shared parameter the user
/// nominated, as the adapter read it, type-level or instance-level, under the
/// Assembly Code link's rule (trimmed, then judged).
/// </summary>
/// <remarks>
/// A parameter the model does not define is simply absent from the readings,
/// and the link answers "unresolved" for every element rather than raising:
/// incoming models are often authored by another discipline, and a missing
/// parameter is ordinary there, not broken.
/// </remarks>
public sealed class SharedParameterResolver : ICodeResolver
{
    private readonly string _parameterName;

    /// <param name="parameterName">The nominated shared parameter, by the name the readings carry it under.</param>
    public SharedParameterResolver(string parameterName)
    {
        _parameterName = Guard.RequiredText(parameterName, nameof(parameterName));
    }

    public string? Resolve(ElementTakeoff element)
    {
        string? trimmed = element.Codes.SharedParameters.TryGetValue(_parameterName, out string? value) ? value?.Trim() : null;
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
