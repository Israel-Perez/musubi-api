using System;
using System.Net;
using Musubi.Api.Platform.Email;

namespace Musubi.Api.Modules.Auth;

/// <summary>Correos de M-01 (texto plano + HTML minimo; el nombre se codifica, nunca se inyecta).</summary>
internal static class AuthEmails
{
    /// <summary>Codigo de verificacion del correo institucional (HU-03).</summary>
    /// <param name="to">Destinatario.</param>
    /// <param name="fullName">Nombre del usuario.</param>
    /// <param name="code">Codigo de 6 digitos.</param>
    /// <returns>El correo.</returns>
    public static EmailMessage VerificationCode(string to, string fullName, string code)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        var firstName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var first, ..] ? first : fullName;
        var minutes = (int)VerificationCodes.Lifetime.TotalMinutes;

        var text =
            $"Hola, {firstName}:\n\n" +
            $"Tu código para verificar tu correo en Musubi es: {code}\n\n" +
            $"Vence en {minutes} minutos. Si no creaste una cuenta, ignora este correo.\n\n" +
            "Musubi · Compra, vende y pide en tu campus.";

        var html =
            $"<p>Hola, {WebUtility.HtmlEncode(firstName)}:</p>" +
            "<p>Tu código para verificar tu correo en Musubi es:</p>" +
            $"<p style=\"font-size:28px;font-weight:800;letter-spacing:6px;color:#D7191F\">{code}</p>" +
            $"<p>Vence en {minutes} minutos. Si no creaste una cuenta, ignora este correo.</p>" +
            "<p style=\"color:#6B6B70\">Musubi · Compra, vende y pide en tu campus.</p>";

        return new EmailMessage(to, $"Tu código de Musubi: {code}", text, html);
    }
}
