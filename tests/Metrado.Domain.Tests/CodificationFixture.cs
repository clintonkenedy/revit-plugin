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
}
