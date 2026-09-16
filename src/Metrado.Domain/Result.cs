namespace Metrado.Domain;

/// <summary>
/// The single result shape used across the Domain seam. Hand-rolled because this
/// assembly carries no third-party dependencies (decision D1), and deliberately
/// two-arity everywhere so a caller never has to guess which overload it holds.
/// </summary>
/// <remarks>
/// The accessors refuse to serve the side the result does not hold. Returning
/// <c>default(TValue)</c> from a failed result would manufacture a plausible
/// number that was never computed — the precise failure mode this product exists
/// to prevent. A third, <see cref="State.Uninitialized"/> state exists because
/// every struct in C# has a reachable zero value, and that zero value must not
/// impersonate a failure carrying a null error.
/// </remarks>
public readonly record struct Result<TValue, TError>
{
    private enum State : byte
    {
        Uninitialized = 0,
        Ok = 1,
        Err = 2,
    }

    private readonly State _state;
    private readonly TValue _value;
    private readonly TError _error;

    private Result(State state, TValue value, TError error)
    {
        _state = state;
        _value = value;
        _error = error;
    }

    /// <summary>Whether this result carries a value. A zero-value struct is never Ok.</summary>
    public bool IsOk => _state == State.Ok;

    /// <summary>The success payload.</summary>
    /// <exception cref="InvalidOperationException">The result does not carry a value.</exception>
    public TValue Value => _state == State.Ok
        ? _value
        : throw new InvalidOperationException(
            $"Result carries no value: it is {_state}. Read Error when the result is Err.");

    /// <summary>The failure payload.</summary>
    /// <exception cref="InvalidOperationException">The result does not carry an error.</exception>
    public TError Error => _state == State.Err
        ? _error
        : throw new InvalidOperationException(
            $"Result carries no error: it is {_state}. Read Value when the result is Ok.");

    public static Result<TValue, TError> Ok(TValue value) => new(State.Ok, value, default!);

    public static Result<TValue, TError> Err(TError error) => new(State.Err, default!, error);

    /// <summary>
    /// Overridden because the synthesized record <c>ToString</c> prints every
    /// public member, which would invoke the accessor that refuses. Diagnostics
    /// — including test failure messages — must never throw.
    /// </summary>
    public override string ToString() => _state switch
    {
        State.Ok => $"Ok({_value})",
        State.Err => $"Err({_error})",
        _ => "Result(uninitialized)",
    };
}
