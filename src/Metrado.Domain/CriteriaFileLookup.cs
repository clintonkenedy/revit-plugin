namespace Metrado.Domain;

/// <summary>
/// What looking for the criteria file produced: the text it held, nothing at all,
/// or a file that is there and refuses to be read.
/// </summary>
/// <remarks>
/// Three states rather than the "text or null" the data-flow diagram originally
/// carried. Null has room for exactly two facts, and the locator has three, so
/// "the file exists but the process could not read it" — permissions, an
/// exclusive lock, a failing disk — had to borrow null from "there is no file".
/// Those two demand opposite responses: <c>takeoff-configuration</c> requires
/// absence to fall back to the built-in defaults, and requires a file that was
/// supplied but could not be honoured to stop the run, because "a silently
/// ignored configuration produces a confidently wrong budget". Sharing one
/// representation means the run picks one of those and is wrong about the other.
/// It picks the fallback, and the failure is invisible: the export succeeds, the
/// workbook looks finished, and every quantity in it was measured under criteria
/// the estimator replaced months ago.
/// <para>
/// <see cref="Match{T}"/> is the only way to the text and to the error, so the
/// three states cannot be sorted into two on the way out. See
/// <see cref="SourceSelection"/> for the same device over two states; the
/// difference is that this type deliberately exposes no discriminator at all,
/// because any single boolean would partition three states into two and re-open
/// the hole in one honest-looking line.
/// </para>
/// <para>
/// This is the result shape only. The file system is never touched here: Domain
/// stays pure, and the locator that produces these values lives in
/// <c>Metrado.Revit2027</c>, which is the assembly allowed to fail at I/O.
/// </para>
/// </remarks>
public sealed class CriteriaFileLookup
{
    /// <remarks>
    /// Numbered from 1 so the zero value names no state. Every field in C# has a
    /// reachable zero, and a zero that silently means <see cref="State.Found"/>
    /// would be a lookup claiming to hold text it never received.
    /// </remarks>
    private enum State : byte
    {
        Found = 1,
        Absent = 2,
        Unreadable = 3,
    }

    private static readonly CriteriaFileLookup NotThere = new(State.Absent, text: null, error: null);

    private readonly State _state;
    private readonly string? _text;
    private readonly ConfigError? _error;

    private CriteriaFileLookup(State state, string? text, ConfigError? error)
    {
        _state = state;
        _text = text;
        _error = error;
    }

    /// <summary>The criteria file was there, and this is the text it held.</summary>
    /// <param name="text">
    /// What was read. Empty and whitespace are accepted: an empty file was still
    /// supplied, and the only other state it could occupy is
    /// <see cref="Absent"/>, which would fall back to defaults for a file that
    /// exists. It is invalid JSON, so it stops the run at the parser, where the
    /// error can name the file and the failing location.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="text"/> is null, which is not a reading of anything.
    /// </exception>
    public static CriteriaFileLookup Found(string text) =>
        new(State.Found, Guard.RequiredValue(text, nameof(text)), error: null);

    /// <summary>
    /// No criteria file was there to read — the one state that may fall back to the
    /// built-in defaults, since "absence of configuration MUST NOT be an error".
    /// </summary>
    public static CriteriaFileLookup Absent => NotThere;

    /// <summary>
    /// The file is there and could not be read. This stops the run.
    /// </summary>
    /// <param name="error">
    /// Why, naming the file. Required: this state exists to stop the run, and
    /// stopping it without saying which file failed is worse than the fallback it
    /// replaced.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="error"/> is null.</exception>
    public static CriteriaFileLookup Unreadable(ConfigError error) =>
        new(State.Unreadable, text: null, Guard.RequiredValue(error, nameof(error)));

    /// <summary>
    /// Collapses all three states into one value, forcing the caller to say what an
    /// unreadable file means before it can look at one that was read.
    /// </summary>
    /// <param name="found">Given the text that was read.</param>
    /// <param name="absent">Invoked when there was no file.</param>
    /// <param name="unreadable">Given the error that stops the run.</param>
    /// <exception cref="ArgumentException">
    /// Any branch is null. All three are checked whichever state this lookup is in,
    /// so a caller cannot discover on the first locked file that it never supplied a
    /// way to handle one.
    /// </exception>
    public T Match<T>(Func<string, T> found, Func<T> absent, Func<ConfigError, T> unreadable)
    {
        Guard.RequiredValue(found, nameof(found));
        Guard.RequiredValue(absent, nameof(absent));
        Guard.RequiredValue(unreadable, nameof(unreadable));

        return _state switch
        {
            State.Found => found(_text!),
            State.Absent => absent(),
            _ => unreadable(_error!),
        };
    }

    /// <summary>
    /// Overridden so a failing assertion names the state.
    /// </summary>
    /// <remarks>
    /// The inherited implementation prints the type name for all three states, which
    /// would render an absent file and an unreadable one identically in exactly the
    /// diagnostics meant to tell them apart. The text is summarised, never echoed: a
    /// criteria file can be large, and this is a label, not a dump.
    /// </remarks>
    public override string ToString() => _state switch
    {
        State.Found => $"Found ({_text!.Length} characters)",
        State.Absent => "Absent",
        _ => $"Unreadable: {_error!.Message}",
    };
}
