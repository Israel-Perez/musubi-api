using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Musubi.Api.Modules.Orders;

// HU-55 (D-01): la cuenta para recibir pagos. Musubi no procesa el dinero: solo muestra la CLABE de quien vende a quien
// le compra por transferencia. La CLABE se valida (dígito verificador y banco), se cifra con una llave que vive solo en
// el back (Parameter Store /musubi/prod/Payments/EncryptionKey) y se muestra enmascarada a su titular.

/// <summary>La CLABE interbancaria: 3 del banco, 3 de la plaza, 11 de la cuenta y el dígito verificador.</summary>
internal static class Clabe
{
    /// <summary>Digitos.</summary>
    public const int Length = 18;

    // Pesos del dígito verificador (Banxico): 3, 7, 1 repetidos sobre los primeros 17 digitos.
    private static readonly int[] Weights = [3, 7, 1, 3, 7, 1, 3, 7, 1, 3, 7, 1, 3, 7, 1, 3, 7];

    /// <summary>Quita espacios y guiones ("012 180 0154 8821 4821").</summary>
    /// <param name="value">Lo que escribio la persona.</param>
    /// <returns>Solo los caracteres que no son espacio ni guion.</returns>
    public static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    /// <summary>Si son 18 digitos con su dígito verificador correcto.</summary>
    /// <param name="clabe">La CLABE ya normalizada.</param>
    /// <returns><c>true</c> si es valida.</returns>
    public static bool IsValid(string clabe) =>
        clabe.Length == Length && clabe.All(char.IsAsciiDigit) && CheckDigit(clabe) == clabe[^1] - '0';

    /// <summary>El dígito verificador de los primeros 17 digitos.</summary>
    /// <param name="clabe">Al menos 17 digitos.</param>
    /// <returns>De 0 a 9.</returns>
    public static int CheckDigit(string clabe)
    {
        ArgumentNullException.ThrowIfNull(clabe);
        var sum = 0;
        for (var i = 0; i < Weights.Length; i++)
        {
            sum += (clabe[i] - '0') * Weights[i] % 10;
        }

        return (10 - sum % 10) % 10;
    }

    /// <summary>"012 180 0154 8821 4821" (como la maqueta).</summary>
    /// <param name="clabe">Los 18 digitos.</param>
    /// <returns>En grupos de 3, 3, 4, 4 y 4.</returns>
    public static string Format(string clabe) =>
        clabe.Length == Length ? $"{clabe[..3]} {clabe[3..6]} {clabe[6..10]} {clabe[10..14]} {clabe[14..]}" : clabe;
}

/// <summary>
/// Los bancos de la CLABE: clave de 3 digitos de los participantes de SPEI (Banxico,
/// <c>banxico.org.mx/cep-scl/listaInstituciones.do</c>, consultada el 15-Nov-2026; la clave de la CLABE son los ultimos
/// 3 digitos de la clave de la institucion).
/// </summary>
internal static class MexicanBanks
{
    /// <summary>Clave → nombre corto, en el orden en que se muestran.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("002", "Banamex"), ("012", "BBVA México"), ("014", "Santander"), ("021", "HSBC"), ("072", "Banorte"),
        ("044", "Scotiabank"), ("036", "Inbursa"), ("030", "BanBajío"), ("127", "Banco Azteca"), ("137", "BanCoppel"),
        ("058", "Banregio"), ("167", "Hey Banco"), ("638", "Nu México"), ("722", "Mercado Pago"), ("728", "Spin by OXXO"),
        ("661", "Klar"), ("138", "Ualá"), ("699", "Fondeadora"), ("721", "albo"), ("723", "Cuenca"), ("646", "STP"),
        ("166", "Banco del Bienestar"), ("130", "Compartamos"), ("062", "Afirme"), ("042", "Mifel"), ("059", "Invex"),
        ("060", "Bansí"), ("106", "Bank of America"), ("108", "MUFG"), ("110", "J.P. Morgan"), ("112", "Monex"),
        ("113", "Ve por Más"), ("124", "Citi México"), ("128", "Kapital"), ("129", "Barclays"), ("132", "Multiva"),
        ("133", "Actinver"), ("136", "Intercam Banco"), ("140", "Consubanco"), ("141", "Volkswagen Bank"),
        ("143", "CIBanco"), ("145", "Banco Base"), ("147", "Bankaool"), ("148", "PagaTodo"), ("150", "Inmobiliario"),
        ("151", "Dondé"), ("152", "Bancrea"), ("154", "Banco Covalto"), ("155", "ICBC"), ("156", "Sabadell"),
        ("157", "Shinhan"), ("158", "Mizuho"), ("159", "Bank of China"), ("160", "Banco S3"), ("168", "Hipotecaria Federal"),
        ("170", "Revolut"), ("006", "Bancomext"), ("009", "Banobras"), ("019", "Banjercito"), ("600", "Monex CB"),
        ("601", "GBM"), ("602", "Masari"), ("605", "Value"), ("616", "Finamex"), ("617", "Valmex"), ("620", "Profuturo"),
        ("634", "Fincomún"), ("652", "Credicapital"), ("653", "Kuspit"), ("656", "Unagra"), ("659", "ASP Integra"),
        ("660", "Altor"), ("670", "Libertad"), ("677", "Caja Popular Mexicana"), ("680", "Cristóbal Colón"),
        ("683", "Caja Telefonistas"), ("684", "Transfer"), ("685", "FIRA"), ("688", "Crediclub"), ("703", "Tesored"),
        ("706", "Arcus"), ("710", "NVIO"), ("714", "PPBalance"), ("715", "Cashi"), ("720", "MexPago"),
        ("725", "Coopdesarrollo"), ("727", "Transfer Directo"), ("729", "Dep y Pag Dig"), ("730", "Clip"),
        ("732", "Peibo"), ("734", "Finco Pay"), ("738", "Fintoc"),
    ];

    private static readonly Dictionary<string, string> ByCode = All.ToDictionary(b => b.Code, b => b.Name, StringComparer.Ordinal);

    /// <summary>El banco de una CLABE, o <c>null</c> si su clave no es de un participante de SPEI.</summary>
    /// <param name="clabe">La CLABE (al menos 3 digitos).</param>
    /// <returns>El nombre o <c>null</c>.</returns>
    public static string? NameOf(string clabe) =>
        clabe.Length >= 3 && ByCode.TryGetValue(clabe[..3], out var name) ? name : null;
}

/// <summary>"Debe coincidir con tu nombre verificado" (maqueta <c>Cuenta-pagos</c>).</summary>
internal static class HolderNames
{
    /// <summary>Largo maximo del titular.</summary>
    public const int MaxLength = 80;

    /// <summary>
    /// El titular es la misma persona: al menos dos palabras y todas estan en su nombre verificado, sin importar
    /// mayusculas ni acentos ("Ana Sofia Ramirez" o "Ana Ramírez" para "Ana Sofía Ramírez López").
    /// </summary>
    /// <param name="holder">El titular que escribio.</param>
    /// <param name="verifiedName">Su nombre completo del registro.</param>
    /// <returns><c>true</c> si coincide.</returns>
    public static bool Matches(string holder, string verifiedName)
    {
        var words = Words(holder);
        var verified = Words(verifiedName).ToHashSet(StringComparer.Ordinal);
        return words.Count >= 2 && words.All(verified.Contains);
    }

    /// <summary>El titular sin espacios de mas.</summary>
    /// <param name="holder">Lo que escribio.</param>
    /// <returns>Las palabras separadas por un espacio.</returns>
    public static string Clean(string? holder) =>
        string.Join(' ', (holder ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static List<string> Words(string value)
    {
        var plain = new StringBuilder();
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToUpperInvariant(c));
            }
        }

        return [.. plain.ToString().Split([' ', '-', '.'], StringSplitOptions.RemoveEmptyEntries)];
    }
}

/// <summary>Configuracion de los pagos (seccion <c>Payments</c>).</summary>
internal sealed class PaymentsOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Payments";

    /// <summary>
    /// Llave AES-256 en base64 (32 bytes) para cifrar la CLABE. Secreto: user-secrets en local y Parameter Store
    /// <c>/musubi/prod/Payments/EncryptionKey</c> en la nube; nunca en appsettings versionados.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;
}

/// <summary>
/// Cifra la CLABE con AES-256-GCM. El texto guardado es <c>v1:</c> + base64(nonce de 12 | cifrado | etiqueta de 16) y el
/// id de la persona va como dato asociado: una CLABE copiada a la cuenta de otra persona no se puede descifrar.
/// Sin llave (o con una invalida) la transferencia queda apagada y el efectivo sigue igual (RNF-15). En Development,
/// sin llave, usa una temporal (los datos en memoria tambien se pierden al reiniciar).
/// </summary>
internal sealed partial class ClabeProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? key;
    private readonly ILogger<ClabeProtector> logger;

    /// <summary>Lee la llave de la configuracion.</summary>
    /// <param name="options">La seccion <c>Payments</c>.</param>
    /// <param name="environment">El ambiente (la llave temporal solo en Development).</param>
    /// <param name="logger">Bitacora (nunca la llave ni la CLABE).</param>
    public ClabeProtector(IOptions<PaymentsOptions> options, IHostEnvironment environment, ILogger<ClabeProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        this.logger = logger;
        var configured = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsDevelopment())
            {
                key = RandomNumberGenerator.GetBytes(32);
                LogTemporaryKey(logger);
            }
            else
            {
                LogMissingKey(logger);
            }

            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(configured.Trim());
            if (bytes.Length == 32)
            {
                key = bytes;
                return;
            }
        }
        catch (FormatException)
        {
            // Cae al aviso de abajo: la llave no es base64.
        }

        LogInvalidKey(logger);
    }

    /// <summary>Si hay llave: sin ella no se guardan cuentas ni se paga por transferencia.</summary>
    public bool Enabled => key is not null;

    /// <summary>Cifra la CLABE de una persona.</summary>
    /// <param name="userId">La persona (dato asociado).</param>
    /// <param name="clabe">Los 18 digitos.</param>
    /// <returns>El texto que se guarda.</returns>
    public string Protect(string userId, string clabe)
    {
        var secret = key ?? throw new InvalidOperationException("Sin llave para cifrar la CLABE.");
        var plain = Encoding.UTF8.GetBytes(clabe);
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(secret, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length), Encoding.UTF8.GetBytes(userId));
        return Prefix + Convert.ToBase64String(output);
    }

    /// <summary>Descifra la CLABE de una persona.</summary>
    /// <param name="userId">La persona (dato asociado).</param>
    /// <param name="ciphertext">Lo que se guardo.</param>
    /// <returns>La CLABE, o <c>null</c> sin llave, con otra llave o si el texto no es de esa persona.</returns>
    public string? Unprotect(string userId, string ciphertext)
    {
        if (key is null || ciphertext is null || !ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var data = Convert.FromBase64String(ciphertext[Prefix.Length..]);
            if (data.Length <= NonceSize + TagSize)
            {
                return null;
            }

            var length = data.Length - NonceSize - TagSize;
            var plain = new byte[length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, length), data.AsSpan(NonceSize + length), plain, Encoding.UTF8.GetBytes(userId));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            LogUnreadable(logger);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pagos: llave temporal para la CLABE (solo Development; las cuentas se pierden al reiniciar)")]
    private static partial void LogTemporaryKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pagos: sin Payments:EncryptionKey, la transferencia queda apagada (el efectivo sigue)")]
    private static partial void LogMissingKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pagos: Payments:EncryptionKey no es una llave base64 de 32 bytes; la transferencia queda apagada")]
    private static partial void LogInvalidKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pagos: una CLABE guardada no se pudo descifrar (otra llave)")]
    private static partial void LogUnreadable(ILogger logger);
}
