namespace OtaLink.Application.Common;

/// <summary>Tipo de error: el controlador lo traduce a un código HTTP (400, 404, 409).</summary>
public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
}

/// <summary>
/// Error de negocio. <see cref="Code"/> es estable (lo usan los clientes y las pruebas); es el mismo
/// código que devuelven los procedimientos de la base de datos. <see cref="Message"/> es el texto
/// para la persona usuaria.
/// </summary>
public sealed record AppError(string Code, string Message, ErrorType Type)
{
    public static readonly AppError None = new(string.Empty, string.Empty, ErrorType.None);
}

/// <summary>
/// Resultado de una operación: éxito o un <see cref="AppError"/>. Un dato inválido de una persona
/// usuaria es un caso esperado, no una excepción.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess != (error == AppError.None))
        {
            throw new ArgumentException("Un resultado exitoso no lleva error y uno fallido sí.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public AppError Error { get; }

    public static Result Success() => new(true, AppError.None);

    public static Result Failure(AppError error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(AppError error) => new(error);
}

/// <summary>Resultado que, si tuvo éxito, trae un valor.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value)
        : base(true, AppError.None) => _value = value;

    internal Result(AppError error)
        : base(false, error)
    {
    }

    /// <summary>El valor. Falla si el resultado es un error: hay que revisar <see cref="Result.IsSuccess"/> antes.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Un resultado fallido no tiene valor.");
}
