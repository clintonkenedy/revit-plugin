namespace Metrado.Domain;

/// <summary>
/// Where in a configuration file the failure was detected.
/// </summary>
/// <remarks>
/// Plain numbers, not a parser exception. Lines and positions count from 1, as
/// an editor shows them (positions in characters); <c>Metrado.Configuration</c>
/// converts the JSON reader's 0-based byte numbers into them. Carrying
/// the exception itself would drag <c>System.Text.Json</c> into Domain, and on
/// the netstandard2.0 leg that package is not in-box — it pulls five more
/// assemblies and falsifies decision D1.
/// </remarks>
public sealed record ConfigLocation
{
    public ConfigLocation(long line, long position)
    {
        if (line < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line), line, "Line must not be negative.");
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Position must not be negative.");
        }

        Line = line;
        Position = position;
    }

    public long Line { get; }

    public long Position { get; }
}

/// <summary>
/// An invalid configuration. This is the only condition that stops a run, so it
/// carries enough to point the user at the exact entry to fix.
/// </summary>
/// <remarks>
/// It lives in Domain rather than in <c>Metrado.Configuration</c> because
/// <c>OpeningsThreshold.TryCreate</c> and <c>Merge</c> return it, and Domain
/// cannot reference the assembly that parses files.
/// <para>
/// Only the message is required: an error raised by a value object has no file
/// and no position to report, while a parse error has all four.
/// </para>
/// </remarks>
public sealed record ConfigError
{
    public ConfigError(string message)
    {
        Message = Guard.RequiredText(message, nameof(message));
    }

    /// <summary>What is wrong, in terms the estimator can act on.</summary>
    public string Message { get; }

    /// <summary>The configuration file, when the error came from one.</summary>
    public string? FilePath { get; init; }

    /// <summary>Where in that file, when the failure has a position.</summary>
    public ConfigLocation? Location { get; init; }

    /// <summary>The offending category, when the error is attributable to one.</summary>
    public string? Category { get; init; }

    /// <summary>The rejected value, rendered as text so any value type can be named.</summary>
    public string? InvalidValue { get; init; }
}
