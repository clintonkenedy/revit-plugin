using System.Reflection;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>takeoff-configuration</c>, requirement "Invalid Configuration Fails Loudly":
/// the system "MUST NOT silently fall back to defaults when a file was supplied,
/// because a silently ignored configuration produces a confidently wrong budget".
/// These tests pin the type that keeps a file which exists but cannot be read from
/// collapsing into the absence that legitimately falls back (residual finding N3).
/// </summary>
public sealed class CriteriaFileLookupTests
{
    /// <summary>
    /// The collapse N3 names: an unreadable file must never reach the branch that
    /// falls back to the built-in defaults.
    /// </summary>
    /// <remarks>
    /// The fallback callback throws, so the only way this test can pass is that it
    /// was not invoked at all.
    /// </remarks>
    [Fact]
    public void AnUnreadableFileNeverReachesTheBranchThatFallsBackToDefaults()
    {
        CriteriaFileLookup lookup = CriteriaFileLookup.Unreadable(
            new ConfigError("Access to the criteria file was denied.")
            {
                FilePath = "/etc/metrado/criteria.json",
            });

        string decision = lookup.Match(
            found: text => $"parse {text}",
            absent: () => throw new Xunit.Sdk.XunitException(
                "An unreadable criteria file fell back to the built-in defaults, "
                    + "which is the silent fallback the spec forbids outright."),
            unreadable: error => $"stop: {error.Message}");

        Assert.Equal("stop: Access to the criteria file was denied.", decision);
    }

    /// <summary>
    /// The companion direction: a genuinely absent file is the one state that may
    /// fall back, and it must never be mistaken for a failure that stops the run.
    /// </summary>
    /// <remarks>
    /// Without this, a <c>Match</c> that always took the <c>unreadable</c> branch
    /// would satisfy the test above perfectly while stopping every first run on a
    /// machine that has no criteria file — the "Missing file falls back to
    /// defaults" scenario, broken in the opposite direction.
    /// </remarks>
    [Fact]
    public void AnAbsentFileFallsBackToDefaultsAndNeverStopsTheRun()
    {
        string decision = CriteriaFileLookup.Absent.Match(
            found: text => $"parse {text}",
            absent: () => "built-in defaults",
            unreadable: error => throw new Xunit.Sdk.XunitException(
                $"An absent criteria file stopped the run reporting '{error.Message}', "
                    + "but absence of configuration MUST NOT be an error."));

        Assert.Equal("built-in defaults", decision);
    }

    /// <summary>
    /// The third state carries the text that was actually read, and hands it to the
    /// branch that parses it.
    /// </summary>
    /// <remarks>
    /// This is the state the other two are defined against: without it, "a file was
    /// supplied" has no representation at all and every run would be a fallback.
    /// </remarks>
    [Fact]
    public void AReadFileHandsItsOwnTextToTheBranchThatParsesIt()
    {
        CriteriaFileLookup lookup = CriteriaFileLookup.Found("{ \"Walls\": { \"threshold\": 1.0 } }");

        string decision = lookup.Match(
            found: text => $"parse {text}",
            absent: () => throw new Xunit.Sdk.XunitException(
                "A criteria file that was read fell back to the built-in defaults, "
                    + "so the file the user supplied was silently ignored."),
            unreadable: error => throw new Xunit.Sdk.XunitException(
                $"A criteria file that was read stopped the run reporting '{error.Message}'."));

        Assert.Equal("parse { \"Walls\": { \"threshold\": 1.0 } }", decision);
    }

    /// <summary>
    /// A reading of null is not a reading. Nothing was read, so the state that says
    /// something was read must refuse to be built.
    /// </summary>
    /// <remarks>
    /// Left unguarded, the null would surface much later as a parse failure blaming
    /// the user's file for the locator's own bug.
    /// </remarks>
    [Fact]
    public void AReadingOfNullIsNotAReadingAndCannotBecomeAFoundFile()
    {
        ArgumentException refused =
            Assert.Throws<ArgumentException>(() => CriteriaFileLookup.Found(null!));

        Assert.Equal("text", refused.ParamName);
    }

    /// <summary>
    /// An empty criteria file was still supplied by the user, so it is a reading of
    /// no characters — never an absence.
    /// </summary>
    /// <remarks>
    /// This is where the guard on <see cref="CriteriaFileLookup.Found"/> has to stop.
    /// Rejecting empty or whitespace text would leave a readable empty file with no
    /// state to occupy, and the locator would have to report it as
    /// <see cref="CriteriaFileLookup.Absent"/> — falling back to defaults for a file
    /// that exists, which is precisely the silent fallback N3 forbids. An empty file
    /// is invalid JSON, so it stops the run at the parser (task 2.1) where the error
    /// can name the file and the failing location.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void AFileThatExistsButHoldsNoUsableTextIsFoundAndNeverAbsent(string emptyish)
    {
        string decision = CriteriaFileLookup.Found(emptyish).Match(
            found: text => $"parse {text.Length} characters",
            absent: () => throw new Xunit.Sdk.XunitException(
                "An empty criteria file was reported as absent, so a file the user "
                    + "supplied was silently replaced by the built-in defaults."),
            unreadable: error => throw new Xunit.Sdk.XunitException(
                $"An empty criteria file was reported unreadable: '{error.Message}'."));

        Assert.Equal($"parse {emptyish.Length} characters", decision);
    }

    /// <summary>
    /// An unreadable file with no error attached cannot be built.
    /// </summary>
    /// <remarks>
    /// This state is the one that stops the run, and the spec requires the message
    /// to identify the file. A null error would stop the run while telling the user
    /// nothing, which is worse than the fallback it replaced.
    /// </remarks>
    [Fact]
    public void AnUnreadableFileWithNothingToReportCannotBeBuilt()
    {
        ArgumentException refused =
            Assert.Throws<ArgumentException>(() => CriteriaFileLookup.Unreadable(null!));

        Assert.Equal("error", refused.ParamName);
    }

    /// <summary>
    /// The error reaches the caller intact, carrying the file it names.
    /// </summary>
    /// <remarks>
    /// Guarding construction proves an error exists; this proves the one the locator
    /// built is the one the run stops on, rather than a replacement invented on the
    /// way out.
    /// </remarks>
    [Fact]
    public void AnUnreadableFileHandsItsOwnErrorToTheBranchThatStopsTheRun()
    {
        ConfigError locked = new("The criteria file is locked by another process.")
        {
            FilePath = "C:\\ProgramData\\Metrado\\criteria.json",
        };

        ConfigError reported = CriteriaFileLookup.Unreadable(locked).Match(
            found: _ => throw new Xunit.Sdk.XunitException(
                "An unreadable criteria file was handed to the parser as if it had been read."),
            absent: () => throw new Xunit.Sdk.XunitException(
                "An unreadable criteria file fell back to the built-in defaults."),
            unreadable: error => error);

        Assert.Same(locked, reported);
        Assert.Equal("C:\\ProgramData\\Metrado\\criteria.json", reported.FilePath);
    }

    /// <summary>
    /// Every branch is required, whichever state the lookup is in.
    /// </summary>
    /// <remarks>
    /// The omitted branch is checked even when it is not the one about to run, so a
    /// caller cannot discover months later — on the first machine whose criteria
    /// file is locked — that it never supplied a way to handle that state.
    /// </remarks>
    [Fact]
    public void MatchRefusesAMissingBranchEvenWhenItIsNotTheBranchThatWouldRun()
    {
        CriteriaFileLookup found = CriteriaFileLookup.Found("{}");

        Assert.Equal(
            "found",
            Assert.Throws<ArgumentException>(
                () => found.Match<string>(null!, () => "absent", _ => "stop")).ParamName);
        Assert.Equal(
            "absent",
            Assert.Throws<ArgumentException>(
                () => found.Match(text => text, null!, _ => "stop")).ParamName);
        Assert.Equal(
            "unreadable",
            Assert.Throws<ArgumentException>(
                () => found.Match(text => text, () => "absent", null!)).ParamName);
    }

    /// <summary>
    /// The three states are distinguishable in a diagnostic message.
    /// </summary>
    /// <remarks>
    /// The inherited <c>ToString</c> prints the type name for all three, so a failing
    /// assertion would report "absent" and "unreadable" identically — the very
    /// confusion this type exists to prevent, reproduced in the tooling meant to
    /// detect it. The found text is summarised rather than echoed: a criteria file
    /// can be large, and a diagnostic is not a place to dump it.
    /// </remarks>
    [Fact]
    public void TheThreeStatesAreTellableApartInADiagnosticMessage()
    {
        Assert.Equal("Found (2 characters)", CriteriaFileLookup.Found("{}").ToString());
        Assert.Equal("Absent", CriteriaFileLookup.Absent.ToString());
        Assert.Equal(
            "Unreadable: Permission denied.",
            CriteriaFileLookup.Unreadable(new ConfigError("Permission denied.")).ToString());
    }

    /// <summary>
    /// Nothing on this type lets a caller sort three states into two outside
    /// <see cref="CriteriaFileLookup.Match{T}"/>.
    /// </summary>
    /// <remarks>
    /// This is the structural half of N3, and it is why this type carries no public
    /// discriminator even though <see cref="SourceSelection"/> deliberately does.
    /// Any single <c>bool</c> — <c>WasFound</c>, <c>HasText</c>, <c>IsAbsent</c> —
    /// partitions three states into two, and every such partition puts an unreadable
    /// file on the same side as one of the other two. The obvious caller,
    /// <c>if (!lookup.WasFound) useDefaults();</c>, is then the exact silent fallback
    /// the spec forbids, written in one honest-looking line. Requiring
    /// <see cref="CriteriaFileLookup.Match{T}"/> makes the compiler demand a decision
    /// for all three.
    /// <para>
    /// Handing out the text or the error directly would reopen the same hole, since
    /// a null return is again two-valued.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoPublicMemberSortsTheThreeStatesIntoTwoOutsideMatch()
    {
        MemberInfo[] surface = typeof(CriteriaFileLookup).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

        // Proves reflection actually inspected the type rather than an empty set.
        Assert.Contains(nameof(CriteriaFileLookup.Match), surface.Select(member => member.Name));

        Type[] twoValued = [typeof(bool), typeof(bool?), typeof(string), typeof(ConfigError)];

        string[] leaks = surface
            // ToString is a label for diagnostics, not a payload accessor, and it is
            // pinned to name all three states rather than split them.
            .Where(member => member.Name != nameof(ToString))
            .Where(member => twoValued.Any(type => Yields(member, type)))
            .Select(member => member.Name)
            .ToArray();

        Assert.True(
            leaks.Length == 0,
            "These members let a caller branch on the lookup without handling all "
                + "three states, so an unreadable criteria file can silently take the "
                + "fallback reserved for an absent one: " + string.Join(", ", leaks));
    }

    /// <summary>Whether reading this member produces a value of the given type.</summary>
    private static bool Yields(MemberInfo member, Type type) => member switch
    {
        PropertyInfo property => property.PropertyType == type,
        FieldInfo field => field.FieldType == type,

        // Covers conversion operators too: op_Implicit and op_Explicit are methods,
        // and an implicit conversion to string would be the quietest leak of all.
        MethodInfo method => method.ReturnType == type,
        _ => false,
    };
}
