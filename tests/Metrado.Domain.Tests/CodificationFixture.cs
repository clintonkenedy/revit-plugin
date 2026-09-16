namespace Metrado.Domain.Tests;

/// <summary>
/// Shared setup for the codification chain's tests.
/// </summary>
/// <remarks>
/// Every codification scenario varies exactly one thing — what the adapter read
/// off the element — while the rest of the takeoff is irrelevant noise. Building
/// the element in one place keeps the reading under test visible instead of buried
/// under seven arguments that never change.
/// </remarks>
internal static class CodificationFixture
{
    /// <summary>A wall whose type carries the given Assembly Code reading.</summary>
    internal static ElementTakeoff WallCoded(string? assemblyCode) =>
        WallRead(assemblyCode: assemblyCode, keynote: null);

    /// <summary>A wall carrying both codification readings the I1 chain can see.</summary>
    internal static ElementTakeoff WallRead(string? assemblyCode, string? keynote) =>
        MeasurementFixture.Wall() with
        {
            Codes = new CodificationReadings(
                assemblyCode: assemblyCode,
                keynote: keynote,
                sharedParameters: new Dictionary<string, string?>()),
        };

    /// <summary>A link that always answers with the same code.</summary>
    /// <remarks>
    /// Stands in for the later links the chain reserves — Keynote, shared
    /// parameter, rule — none of which exist yet. A test double keeps the ordering
    /// rule testable now without this increment guessing at resolvers that later
    /// increments own.
    /// </remarks>
    internal static ICodeResolver Link(string? code) => new StubResolver(code);

    /// <summary>A link that fails the test if the chain ever consults it.</summary>
    /// <remarks>
    /// "Later resolvers MUST NOT be consulted" is a claim about what did
    /// <em>not</em> happen, and the only way to assert that is to make the
    /// forbidden call loud. A stub that merely recorded the call would still let
    /// the chain read a Keynote it had no business reading.
    /// </remarks>
    internal static ICodeResolver LinkThatMustNotRun(string name) => new ForbiddenResolver(name);

    private sealed class StubResolver(string? code) : ICodeResolver
    {
        public string? Resolve(ElementTakeoff element) => code;
    }

    private sealed class ForbiddenResolver(string name) : ICodeResolver
    {
        public string? Resolve(ElementTakeoff element) =>
            throw new Xunit.Sdk.XunitException(
                $"The chain consulted the {name} link after an earlier link had already resolved a code.");
    }
}
