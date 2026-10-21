using System;

namespace Musubi.Api.Platform.Errors;

/// <summary>Resultado de un caso de uso: un valor o un <see cref="ApiError"/> del catalogo (sin excepciones de negocio, 02-03).</summary>
/// <typeparam name="T">Tipo del valor.</typeparam>
internal readonly record struct Result<T>
{
    private Result(T? value, ApiError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>Valor si tuvo exito.</summary>
    public T? Value { get; }

    /// <summary>Error si fallo.</summary>
    public ApiError? Error { get; }

    /// <summary>Exito.</summary>
    /// <param name="value">Valor.</param>
    /// <returns>El resultado.</returns>
    public static Result<T> Ok(T value) => new(value, null);

    /// <summary>Fallo con un error del catalogo.</summary>
    /// <param name="error">Error.</param>
    /// <returns>El resultado.</returns>
    public static Result<T> Fail(ApiError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>Conversion implicita desde un error.</summary>
    /// <param name="error">Error.</param>
    public static implicit operator Result<T>(ApiError error) => Fail(error);
}
