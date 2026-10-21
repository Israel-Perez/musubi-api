using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

namespace Musubi.Api.Modules.Auth;

/// <summary>Correo normalizado: minusculas, sin espacios y con su dominio (RNF-21: el dominio identifica el campus).</summary>
/// <param name="Address">Correo normalizado.</param>
/// <param name="Domain">Dominio en minusculas.</param>
internal sealed record NormalizedEmail(string Address, string Domain);

/// <summary>Normaliza correos para el indice unico y la busqueda del campus.</summary>
internal static class EmailAddresses
{
    /// <summary>Intenta normalizar; rechaza nombres para mostrar ("Ana &lt;a@b.c&gt;") y formatos invalidos.</summary>
    /// <param name="input">Lo que escribio el usuario.</param>
    /// <param name="email">El correo normalizado.</param>
    /// <returns><c>true</c> si es un correo simple valido.</returns>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out NormalizedEmail? email)
    {
        email = null;
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 254 || !MailAddress.TryCreate(trimmed, out var parsed))
        {
            return false;
        }

        // MailAddress acepta "Nombre <correo>": solo se admite la direccion sola.
        if (!string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase) || !parsed.Host.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var address = parsed.Address.ToLowerInvariant();
        email = new NormalizedEmail(address, parsed.Host.ToLowerInvariant());
        return true;
    }
}
