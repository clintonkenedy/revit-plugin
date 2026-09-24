namespace Metrado.Domain;

/// <summary>
/// The criteria a run measured under, together with where they came from.
/// </summary>
/// <remarks>
/// <see cref="Source"/> and <see cref="Path"/> are kept consistent by construction
/// rather than by convention, because they are not merely stored — they are read
/// out to the user in the completion report, and each way of letting them disagree
/// produces a specific lie there. See the constructor.
/// </remarks>
public sealed record EffectiveCriteria
{
    /// <param name="criteria">The criteria in force. Required.</param>
    /// <param name="source">Where they came from. Must be a declared member.</param>
    /// <param name="path">
    /// The file, which must be present exactly when <paramref name="source"/> is
    /// <see cref="ConfigSource.File"/> and absent otherwise.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="criteria"/> is null, or <paramref name="path"/> disagrees
    /// with <paramref name="source"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="source"/> is not a declared <see cref="ConfigSource"/>.
    /// </exception>
    public EffectiveCriteria(CriteriaSet criteria, ConfigSource source, string? path)
    {
        Guard.RequiredValue(criteria, nameof(criteria));
        RequireDeclaredSource(source);
        RequireAgreement(source, path);

        Criteria = criteria;
        Source = source;
        Path = path;
    }

    /// <summary>The criteria in force, defaults and file overrides already merged.</summary>
    public CriteriaSet Criteria { get; }

    /// <summary>Whether those criteria came from a file or from the product.</summary>
    public ConfigSource Source { get; }

    /// <summary>The file they were read from, when one was.</summary>
    public string? Path { get; }

    /// <summary>The saved configuration the file names itself as, when it does (task 3.7).</summary>
    /// <exception cref="ArgumentException">Set on criteria that came from no file.</exception>
    public string? ConfigurationName
    {
        get => _configurationName;
        init => _configurationName = FromFileOnly(value, nameof(ConfigurationName));
    }

    /// <summary>The shared parameter the configuration nominates for the chain's third link, when it does.</summary>
    /// <exception cref="ArgumentException">Set on criteria that came from no file.</exception>
    public Guid? SharedParameter
    {
        get => _sharedParameter;
        init => _sharedParameter = FromFileOnly(value, nameof(SharedParameter));
    }

    private readonly string? _configurationName;

    private readonly Guid? _sharedParameter;

    /// <summary>A configuration is a file: the product's own criteria name none, and nominate nothing.</summary>
    private T FromFileOnly<T>(T value, string name) =>
        value is null || Source == ConfigSource.File
            ? value
            : throw new ArgumentException($"The built-in criteria are no saved configuration, so they have no {name}.", name);

    /// <summary>
    /// Refuses a provenance that cannot be named.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <c>Measurement</c>'s refusal of an undeclared boundary
    /// mode: every enum in C# has a reachable zero, and a source rendered as a bare
    /// <c>0</c> in the completion dialog tells the estimator nothing about which
    /// criteria produced the budget they are about to sign.
    /// </remarks>
    private static void RequireDeclaredSource(ConfigSource source)
    {
        if (!Enum.IsDefined(typeof(ConfigSource), source))
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                $"Not a declared configuration source. Expected "
                    + $"{nameof(ConfigSource.BuiltInDefaults)} or {nameof(ConfigSource.File)}.");
        }
    }

    /// <summary>
    /// Refuses the two ways provenance and path can contradict each other.
    /// </summary>
    /// <remarks>
    /// A <see cref="ConfigSource.File"/> run without a path cannot answer "which
    /// configuration was applied?", and there is normally more than one candidate
    /// file on an estimator's machine.
    /// <para>
    /// The reverse is worse. Defaults carrying a path let the completion report read
    /// "criteria from criteria.json" for a run that honoured no file at all — which
    /// is precisely the outcome <c>takeoff-configuration</c> calls "a confidently
    /// wrong budget", except now it is confidently attributed to a file the
    /// estimator will go and inspect. The searched path is a different fact from the
    /// path in force, and it does not belong in this field.
    /// </para>
    /// </remarks>
    private static void RequireAgreement(ConfigSource source, string? path)
    {
        if (source == ConfigSource.File && path is null)
        {
            throw new ArgumentException(
                "Criteria read from a file must name the file, because the run has to "
                    + "report which configuration it applied.",
                nameof(path));
        }

        if (source == ConfigSource.BuiltInDefaults && path is not null)
        {
            throw new ArgumentException(
                $"The built-in defaults were applied, so no file is in force, but '{path}' "
                    + "was given as their path. Reporting a path here would attribute the "
                    + "budget to a file that was never honoured.",
                nameof(path));
        }
    }
}
