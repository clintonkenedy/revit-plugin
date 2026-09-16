namespace Metrado.Domain;

/// <summary>
/// Construction guards shared by the Domain value types.
/// </summary>
/// <remarks>
/// Hand-rolled because <c>ArgumentException.ThrowIfNullOrWhiteSpace</c> is .NET 8+
/// and this assembly still compiles for <c>netstandard2.0</c> (decision D1).
/// <para>
/// Null and blank raise the same <see cref="ArgumentException"/> on purpose: to a
/// caller they are one rule — "this text identifies something, so it must carry
/// characters" — and splitting them would only invite catching one and missing
/// the other.
/// </para>
/// </remarks>
internal static class Guard
{
    /// <summary>Returns <paramref name="value"/> when it carries non-whitespace text.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    internal static string RequiredText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} must be non-empty.", name);
        }

        return value!;
    }

    /// <summary>Returns <paramref name="value"/> when it is not null.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null.</exception>
    internal static T RequiredValue<T>(T? value, string name)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentException($"{name} must not be null.", name);
        }

        return value;
    }
}
