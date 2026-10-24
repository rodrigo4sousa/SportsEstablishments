namespace SportsBooking.Application.Common;

/// <summary>What kind of expected failure a <see cref="ServiceError"/> is; the API maps it to a status code.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    NotFound,
    Conflict,

    /// <summary>The request is fine but a business rule refuses it (422), e.g. removing a block that has started.</summary>
    Unprocessable,
}

/// <summary>
/// An expected failure returned by a service (never thrown). <see cref="Code"/> is the stable ProblemDetails code;
/// <see cref="FieldErrors"/> holds per-field messages, keyed by the JSON property name.
/// </summary>
public sealed record ServiceError(
    ErrorKind Kind,
    string Code,
    string Title,
    string Detail,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static ServiceError Validation(IReadOnlyDictionary<string, string[]> fieldErrors) => new(
        ErrorKind.Validation,
        ErrorCodes.ValidationFailed,
        "Dados inválidos",
        "Há campos com valores inválidos.",
        fieldErrors);
}

/// <summary>Either a value or an expected <see cref="ServiceError"/>. Created by implicit conversion from either.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, ServiceError? error)
    {
        _value = value;
        Error = error;
    }

    public ServiceError? Error { get; }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(ServiceError error) => new(default, error);
}
