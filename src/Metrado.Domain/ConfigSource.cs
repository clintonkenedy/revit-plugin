namespace Metrado.Domain;

/// <summary>
/// Where the criteria a run measured under actually came from.
/// </summary>
/// <remarks>
/// <c>takeoff-configuration</c> requires the run to report "that no configuration
/// file was found", which is a fact about provenance rather than about the criteria
/// themselves: the built-in defaults and a file that happens to restate them
/// produce identical criteria and must not produce identical reports.
/// <para>
/// A closed set of two. Resolution has exactly two outcomes that carry criteria —
/// the product's own defaults, or a file — and everything else is a
/// <see cref="ConfigError"/> that stops the run instead of producing a third kind
/// of provenance.
/// </para>
/// </remarks>
public enum ConfigSource
{
    /// <summary>
    /// No configuration file was honoured; <see cref="CriteriaSet.Default"/> is in
    /// force. The one outcome the specification insists is not an error.
    /// </summary>
    BuiltInDefaults = 1,

    /// <summary>A configuration file was read and applied over the defaults.</summary>
    File = 2,
}
