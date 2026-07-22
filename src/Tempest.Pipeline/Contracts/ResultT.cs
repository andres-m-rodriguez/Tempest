using System.Diagnostics.CodeAnalysis;

namespace Tempest.Pipeline;

/// <summary>The outcome of a fallible pipeline call: the value, or the <see cref="IError"/>
/// explaining why there isn't one — never a thrown exception. A readonly record struct,
/// so results compare by value end to end and success flows through nullable analysis
/// (<see cref="IsSuccess"/> proves <see cref="Value"/>).</summary>
public readonly record struct Result<T>
{
    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess { get; }

    [MaybeNull]
    public T Value { get; }

    public IError? Error { get; }

    private Result(bool isSuccess, [AllowNull] T value, IError? error)
    {
        IsSuccess = isSuccess;
        Value = value!;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(IError error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Ok(value);

    /// <summary>Projects a success value; a failure passes through untouched.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => Error is { } error ? Result<TOut>.Fail(error) : Result<TOut>.Ok(map(Value!));
}
