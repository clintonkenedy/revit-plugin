using System.Globalization;

namespace Metrado.Revit2027;

/// <summary>
/// What a smoke run says: a verdict read at a glance, and one line per
/// check, which also go to the journal because it outlives the dialog.
/// Uses no Revit type.
/// </summary>
public static class SmokeReport
{
    /// <summary>A run passes only if nothing failed and something was actually checked.</summary>
    public static string Verdict(IReadOnlyList<SmokeCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        int failed = checks.Count(check => check.Status == SmokeStatus.Fail);
        int passed = checks.Count(check => check.Status == SmokeStatus.Pass);
        int skipped = checks.Count(check => check.Status == SmokeStatus.Skip);

        string counts = failed == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{passed} passed, {skipped} skipped")
            : string.Create(CultureInfo.InvariantCulture, $"{failed} failed, {passed} passed, {skipped} skipped");
        return (failed == 0 && passed > 0 ? "Smoke passed: " : "Smoke FAILED: ") + counts;
    }

    public static IReadOnlyList<string> Lines(IReadOnlyList<SmokeCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        return [.. checks.Select(check => $"{check.Status.ToString().ToUpperInvariant()} {check.Name}: {check.Detail}")];
    }
}
