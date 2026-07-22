namespace Tempest.Pipeline;

/// <summary>The outcome of a fallible pipeline call that yields no value: success, or
/// the <see cref="IError"/> explaining the failure — never a thrown exception.</summary>
public readonly record struct Result
{
    public bool IsSuccess { get; }
    public IError? Error { get; }

    private Result(bool isSuccess, IError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Ok() => new(true, null);

    public static Result Fail(IError error) => new(false, error);
}
