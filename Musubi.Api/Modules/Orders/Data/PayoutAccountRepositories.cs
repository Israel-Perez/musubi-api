using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Orders.Data;

// Cuentas para recibir pagos (HU-55, maqueta Cuenta-pagos). La validacion del servidor vive en db/027-pagos.js; fila
// canonica en docs/modelo-datos.md. Mongo e InMemory pasan la misma bateria (TestShared/Orders).

/// <summary>
/// La cuenta de una persona para que le paguen por transferencia (D-01). La CLABE solo se guarda cifrada (R10) y, para
/// mostrarla enmascarada, sus ultimos 4 digitos.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class PayoutAccountDocument
{
    /// <summary>La persona (una cuenta por persona: el id es el suyo).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Su campus (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Titular, igual a su nombre verificado ("Ana Sofía Ramírez").</summary>
    public string HolderName { get; init; } = string.Empty;

    /// <summary>Clave del banco: los 3 primeros digitos de la CLABE (<c>012</c>).</summary>
    public string BankCode { get; init; } = string.Empty;

    /// <summary>Nombre del banco ("BBVA México").</summary>
    public string BankName { get; init; } = string.Empty;

    /// <summary>La CLABE cifrada con AES-GCM (<see cref="ClabeProtector"/>).</summary>
    public string ClabeCiphertext { get; init; } = string.Empty;

    /// <summary>Ultimos 4 digitos, para "•••• 4821".</summary>
    public string ClabeLast4 { get; init; } = string.Empty;

    /// <summary>Cuando se valido (dígito verificador, banco y titular) y se guardo (UTC).</summary>
    public DateTime VerifiedAt { get; init; }

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultimo cambio (UTC).</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Cuentas para recibir pagos.</summary>
internal interface IPayoutAccountRepository
{
    /// <summary>La cuenta de una persona, o <c>null</c>.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    Task<PayoutAccountDocument?> FindAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Guarda la cuenta de una persona; si ya tenia una, la reemplaza y conserva su alta.</summary>
    /// <param name="account">La cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta como quedo.</returns>
    Task<PayoutAccountDocument> SaveAsync(PayoutAccountDocument account, CancellationToken cancellationToken);

    /// <summary>Quita la cuenta de una persona (tambien al borrar su cuenta, HU-46).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si tenia una.</returns>
    Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre MongoDB.</summary>
internal sealed class MongoPayoutAccountRepository(IMongoDatabase database) : IPayoutAccountRepository
{
    private readonly IMongoCollection<PayoutAccountDocument> accounts = database.GetCollection<PayoutAccountDocument>(MongoSchema.PayoutAccounts);

    /// <inheritdoc />
    public async Task<PayoutAccountDocument?> FindAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await accounts.Find(a => a.Id == userId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<PayoutAccountDocument> SaveAsync(PayoutAccountDocument account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var update = Builders<PayoutAccountDocument>.Update
            .SetOnInsert(a => a.CommunityId, account.CommunityId)
            .SetOnInsert(a => a.CreatedAt, account.CreatedAt)
            .SetOnInsert(a => a.SchemaVersion, account.SchemaVersion)
            .Set(a => a.HolderName, account.HolderName)
            .Set(a => a.BankCode, account.BankCode)
            .Set(a => a.BankName, account.BankName)
            .Set(a => a.ClabeCiphertext, account.ClabeCiphertext)
            .Set(a => a.ClabeLast4, account.ClabeLast4)
            .Set(a => a.VerifiedAt, account.VerifiedAt)
            .Set(a => a.UpdatedAt, account.UpdatedAt);
        return await accounts.FindOneAndUpdateAsync(
            Builders<PayoutAccountDocument>.Filter.Eq(a => a.Id, account.Id),
            update,
            new FindOneAndUpdateOptions<PayoutAccountDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
        && (await accounts.DeleteOneAsync(a => a.Id == userId, cancellationToken).ConfigureAwait(false)).DeletedCount == 1;
}

/// <summary>Cuentas en memoria (Development y pruebas).</summary>
internal sealed class InMemoryPayoutAccountRepository : IPayoutAccountRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, PayoutAccountDocument> accounts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<PayoutAccountDocument?> FindAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(accounts.GetValueOrDefault(userId));
        }
    }

    /// <inheritdoc />
    public Task<PayoutAccountDocument> SaveAsync(PayoutAccountDocument account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        lock (gate)
        {
            var saved = accounts.TryGetValue(account.Id, out var before)
                ? new PayoutAccountDocument
                {
                    Id = account.Id,
                    CommunityId = before.CommunityId,
                    HolderName = account.HolderName,
                    BankCode = account.BankCode,
                    BankName = account.BankName,
                    ClabeCiphertext = account.ClabeCiphertext,
                    ClabeLast4 = account.ClabeLast4,
                    VerifiedAt = account.VerifiedAt,
                    CreatedAt = before.CreatedAt,
                    UpdatedAt = account.UpdatedAt,
                    SchemaVersion = before.SchemaVersion,
                }
                : account;
            accounts[account.Id] = saved;
            return Task.FromResult(saved);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(accounts.Remove(userId));
        }
    }
}
