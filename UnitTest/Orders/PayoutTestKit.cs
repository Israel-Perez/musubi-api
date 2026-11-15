using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Orders;

namespace UnitTest.Orders;

/// <summary>HU-55: piezas de prueba para la cuenta para recibir pagos.</summary>
internal static class PayoutTestKit
{
    /// <summary>Una llave AES-256 de prueba (base64 de 32 bytes fijos; no es un secreto).</summary>
    public static readonly string TestKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    /// <summary>CLABE valida de BBVA México (la de la maqueta <c>Cuenta-pagos</c> con su dígito verificador).</summary>
    public static readonly string BbvaClabe = WithCheckDigit("01218001548821482");

    /// <summary>Completa 17 digitos con su dígito verificador.</summary>
    /// <param name="first17">Los primeros 17.</param>
    /// <returns>Los 18.</returns>
    public static string WithCheckDigit(string first17) => first17 + Clabe.CheckDigit(first17);

    /// <summary>El protector con una llave (o sin ella) en un ambiente.</summary>
    /// <param name="key">La llave, o vacio.</param>
    /// <param name="environment">El ambiente.</param>
    /// <returns>El protector.</returns>
    public static ClabeProtector Protector(string? key = null, string environment = "Production") =>
        new(
            Options.Create(new PaymentsOptions { EncryptionKey = key ?? TestKey }),
            new FakeEnvironment(environment),
            NullLogger<ClabeProtector>.Instance);

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Musubi.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

/// <summary>Quien puede recibir por transferencia, sin cifrado (para los servicios de pedidos).</summary>
internal sealed class FakePayoutDirectory : IPayoutDirectory
{
    /// <summary>Cuentas por persona.</summary>
    public Dictionary<string, TransferAccount> Accounts { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> CanReceiveAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(Accounts.ContainsKey(userId));

    /// <inheritdoc />
    public Task<TransferAccount?> ForTransferAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.GetValueOrDefault(userId));
}
