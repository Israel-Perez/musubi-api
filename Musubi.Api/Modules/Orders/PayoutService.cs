using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>Los datos para transferirle a quien vende (HU-55), ya descifrados. Solo para el pedido que los pide.</summary>
/// <param name="HolderName">Titular.</param>
/// <param name="BankName">Banco.</param>
/// <param name="Clabe">Los 18 digitos.</param>
/// <param name="ClabeLast4">Ultimos 4.</param>
internal sealed record TransferAccount(string HolderName, string BankName, string Clabe, string ClabeLast4);

/// <summary>Lo que los pedidos necesitan de las cuentas para recibir pagos.</summary>
internal interface IPayoutDirectory
{
    /// <summary>Si a una persona le pueden pagar por transferencia (tiene cuenta y hay llave, RNF-15).</summary>
    /// <param name="userId">Quien vende.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si puede.</returns>
    Task<bool> CanReceiveAsync(string userId, CancellationToken cancellationToken);

    /// <summary>La cuenta descifrada de quien vende, o <c>null</c> si no tiene o no se puede leer.</summary>
    /// <param name="userId">Quien vende.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    Task<TransferAccount?> ForTransferAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// "Cuenta para recibir pagos" (HU-55, maqueta <c>Cuenta-pagos</c>, Ajustes): la CLABE se valida (dígito verificador y
/// banco de SPEI), el titular debe ser la persona verificada, se guarda cifrada y solo se muestra enmascarada.
/// </summary>
internal sealed class PayoutService(
    IPayoutAccountRepository accounts,
    IMemberDirectory members,
    ClabeProtector protector,
    TimeProvider time) : IPayoutDirectory
{
    /// <summary>Los bancos que se reconocen.</summary>
    /// <returns>La lista.</returns>
    public static BankListResponse Banks() => new([.. MexicanBanks.All.Select(b => new BankResponse(b.Code, b.Name))]);

    /// <summary>Mi cuenta (enmascarada) y si se pueden guardar cuentas.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El estado.</returns>
    public async Task<PayoutAccountStatus> MineAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        return new PayoutAccountStatus(protector.Enabled, account is null ? null : ToResponse(account));
    }

    /// <summary>"Guardar cuenta": valida, cifra y la guarda (reemplaza la anterior).</summary>
    /// <param name="userId">Quien la registra (del token).</param>
    /// <param name="body">Titular y CLABE.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta enmascarada o el error.</returns>
    public async Task<Result<PayoutAccountResponse>> SaveAsync(string userId, SavePayoutAccountRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true } member)
        {
            return ApiErrors.Forbidden;
        }

        if (!protector.Enabled)
        {
            return PayoutsErrors.Unavailable;
        }

        var holder = HolderNames.Clean(body.HolderName);
        if (holder.Length is < 3 or > HolderNames.MaxLength)
        {
            return PayoutsErrors.InvalidHolder;
        }

        if (!HolderNames.Matches(holder, member.FullName))
        {
            return PayoutsErrors.HolderMismatch;
        }

        var clabe = Clabe.Normalize(body.Clabe);
        if (!Clabe.IsValid(clabe))
        {
            return PayoutsErrors.InvalidClabe;
        }

        if (MexicanBanks.NameOf(clabe) is not { } bank)
        {
            return PayoutsErrors.UnknownBank;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var saved = await accounts.SaveAsync(
            new PayoutAccountDocument
            {
                Id = userId,
                CommunityId = member.CommunityId,
                HolderName = holder,
                BankCode = clabe[..3],
                BankName = bank,
                ClabeCiphertext = protector.Protect(userId, clabe),
                ClabeLast4 = clabe[^4..],
                VerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        return Result<PayoutAccountResponse>.Ok(ToResponse(saved));
    }

    /// <summary>"Quitar cuenta": desde ahi, solo efectivo.</summary>
    /// <param name="userId">Quien la quita (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea (idempotente).</returns>
    public Task DeleteAsync(string userId, CancellationToken cancellationToken) => accounts.DeleteAsync(userId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CanReceiveAsync(string userId, CancellationToken cancellationToken) =>
        protector.Enabled && await accounts.FindAsync(userId, cancellationToken).ConfigureAwait(false) is not null;

    /// <inheritdoc />
    public async Task<TransferAccount?> ForTransferAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        return account is not null && protector.Unprotect(userId, account.ClabeCiphertext) is { } clabe
            ? new TransferAccount(account.HolderName, account.BankName, clabe, account.ClabeLast4)
            : null;
    }

    private static PayoutAccountResponse ToResponse(PayoutAccountDocument account) =>
        new(account.HolderName, account.BankCode, account.BankName, account.ClabeLast4, account.UpdatedAt);
}

/// <summary>Borrado de una cuenta (HU-46, R11): su cuenta para recibir pagos desaparece.</summary>
internal sealed class PayoutAccountEraser(IPayoutAccountRepository accounts) : IAccountDataEraser
{
    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) => accounts.DeleteAsync(userId, cancellationToken);
}
