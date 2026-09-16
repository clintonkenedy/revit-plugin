namespace Metrado.Domain.Tests;

/// <summary>
/// <see cref="Result{TValue, TError}"/> is hand-rolled because Domain carries no
/// third-party dependencies (decision D1). These tests pin the two properties the
/// rest of the seam relies on: one consistent two-arity shape, and the refusal to
/// hand back a payload the result does not actually hold.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void OkCarriesTheSuccessValue()
    {
        Result<int, string> result = Result<int, string>.Ok(42);

        Assert.True(result.IsOk);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ErrCarriesTheErrorValue()
    {
        Result<int, string> result = Result<int, string>.Err("threshold is negative");

        Assert.False(result.IsOk);
        Assert.Equal("threshold is negative", result.Error);
    }

    /// <summary>
    /// Handing back <c>default(TValue)</c> for a failed result is the exact
    /// failure mode this product exists to prevent: a plausible number that was
    /// never measured. Reading the wrong side MUST be loud.
    /// </summary>
    [Fact]
    public void ReadingTheValueOfAnErrorThrows()
    {
        Result<int, string> result = Result<int, string>.Err("threshold is negative");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => result.Value);

        Assert.Contains("Error", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingTheErrorOfAnOkThrows()
    {
        Result<int, string> result = Result<int, string>.Ok(42);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => result.Error);

        Assert.Contains("Ok", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every struct in C# has a reachable zero value. An uninitialized result must
    /// not impersonate a failure carrying a null error, so both sides refuse.
    /// </summary>
    [Fact]
    public void DefaultResultRefusesBothSides()
    {
        Result<int, string> uninitialized = default;

        Assert.False(uninitialized.IsOk);
        Assert.Throws<InvalidOperationException>(() => uninitialized.Value);
        Assert.Throws<InvalidOperationException>(() => uninitialized.Error);
    }

    [Fact]
    public void ResultsAreComparedByTheirPayload()
    {
        Assert.Equal(Result<int, string>.Ok(42), Result<int, string>.Ok(42));
        Assert.NotEqual(Result<int, string>.Ok(42), Result<int, string>.Ok(7));
        Assert.NotEqual(Result<int, string>.Ok(42), Result<int, string>.Err("boom"));
        Assert.Equal(Result<int, string>.Err("boom"), Result<int, string>.Err("boom"));
    }

    /// <summary>
    /// A record's synthesized <c>ToString</c> prints every public member, which
    /// would invoke the very accessor that refuses. Diagnostics must never throw.
    /// </summary>
    [Fact]
    public void ToStringDescribesEitherSideWithoutThrowing()
    {
        Assert.Contains("42", Result<int, string>.Ok(42).ToString(), StringComparison.Ordinal);
        Assert.Contains("boom", Result<int, string>.Err("boom").ToString(), StringComparison.Ordinal);
        Assert.Contains("uninitialized", default(Result<int, string>).ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
