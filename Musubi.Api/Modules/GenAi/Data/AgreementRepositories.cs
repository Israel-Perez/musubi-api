using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.GenAi.Data;

// Contratos de persistencia de M-10 (HU-32/33/34). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true)
// pasan la misma bateria de pruebas (TestShared/GenAi).

/// <summary>Papeles de una persona en el acuerdo.</summary>
internal static class AgreementRoles
{
    /// <summary>Quien compra.</summary>
    public const string Buyer = "buyer";

    /// <summary>Quien vende.</summary>
    public const string Seller = "seller";
}

/// <summary>Acuerdos.</summary>
internal interface IAgreementRepository
{
    /// <summary>
    /// Guarda el acuerdo nuevo de una conversacion en lugar del que hubiera (pedir otro resumen empieza de cero). Si
    /// dos llegan a la vez, gana el primero y se devuelve ese.
    /// </summary>
    /// <param name="agreement">El acuerdo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo guardado.</returns>
    Task<AgreementDocument> ReplaceForConversationAsync(AgreementDocument agreement, CancellationToken cancellationToken);

    /// <summary>Un acuerdo vigente por id.</summary>
    /// <param name="id">Id.</param>
    /// <param name="now">Ahora (UTC): uno vencido ya no cuenta aunque el TTL todavia no lo borre.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo o <c>null</c>.</returns>
    Task<AgreementDocument?> FindByIdAsync(string id, DateTime now, CancellationToken cancellationToken);

    /// <summary>El acuerdo vigente de una conversacion.</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo o <c>null</c>.</returns>
    Task<AgreementDocument?> FindByConversationAsync(string conversationId, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Cambia la propuesta si sigue en <paramref name="revision"/>: sube la version, vuelve a "Esperando confirmacion"
    /// y borra las dos confirmaciones ("Si algo cambia, ambos deben confirmar de nuevo").
    /// </summary>
    /// <param name="id">Id.</param>
    /// <param name="revision">La version que vio quien edita.</param>
    /// <param name="proposal">La propuesta nueva.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo cambiado, o <c>null</c> si ya no existe o cambio antes.</returns>
    Task<AgreementDocument?> UpdateProposalAsync(string id, int revision, AgreementProposal proposal, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Registra la confirmacion de un lado si la propuesta sigue en <paramref name="revision"/>; con los dos queda
    /// <see cref="AgreementStatuses.Confirmed"/> (RN-41).
    /// </summary>
    /// <param name="id">Id.</param>
    /// <param name="revision">La version que vio quien confirma.</param>
    /// <param name="role"><see cref="AgreementRoles.Buyer"/> o <see cref="AgreementRoles.Seller"/>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo, o <c>null</c> si ya no existe o cambio antes.</returns>
    Task<AgreementDocument?> ConfirmAsync(string id, int revision, string role, DateTime now, CancellationToken cancellationToken);

    /// <summary>Borra un acuerdo (descartado o ya convertido en pedido).</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);

    /// <summary>Borra los acuerdos donde participa el usuario (HU-46, R11).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByParticipantAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Acuerdos en MongoDB (coleccion <c>agreements</c>, db/016).</summary>
internal sealed class MongoAgreementRepository(IMongoDatabase database) : IAgreementRepository
{
    private static readonly FilterDefinitionBuilder<AgreementDocument> Filter = Builders<AgreementDocument>.Filter;

    private readonly IMongoCollection<AgreementDocument> agreements = database.GetCollection<AgreementDocument>(MongoSchema.Agreements);

    /// <inheritdoc />
    public async Task<AgreementDocument> ReplaceForConversationAsync(AgreementDocument agreement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        await agreements.DeleteManyAsync(a => a.ConversationId == agreement.ConversationId, cancellationToken).ConfigureAwait(false);
        try
        {
            await agreements.InsertOneAsync(agreement, cancellationToken: cancellationToken).ConfigureAwait(false);
            return agreement;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_conversation: otro resumen de la misma conversacion entro primero.
            return await agreements.Find(a => a.ConversationId == agreement.ConversationId)
                .FirstAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<AgreementDocument?> FindByIdAsync(string id, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await agreements.Find(Filter.Eq(a => a.Id, id) & Filter.Gt(a => a.ExpiresAt, now))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<AgreementDocument?> FindByConversationAsync(string conversationId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(conversationId, out _)
            ? await agreements.Find(Filter.Eq(a => a.ConversationId, conversationId) & Filter.Gt(a => a.ExpiresAt, now))
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<AgreementDocument?> UpdateProposalAsync(string id, int revision, AgreementProposal proposal, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await agreements.FindOneAndUpdateAsync(
                Filter.Eq(a => a.Id, id) & Filter.Eq(a => a.Revision, revision) & Filter.Gt(a => a.ExpiresAt, now),
                Builders<AgreementDocument>.Update
                    .Set(a => a.Proposal, proposal)
                    .Inc(a => a.Revision, 1)
                    .Set(a => a.Status, AgreementStatuses.Proposed)
                    .Unset(a => a.BuyerConfirmedAt)
                    .Unset(a => a.SellerConfirmedAt)
                    .Set(a => a.UpdatedAt, now),
                new FindOneAndUpdateOptions<AgreementDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<AgreementDocument?> ConfirmAsync(string id, int revision, string role, DateTime now, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var current = Filter.Eq(a => a.Id, id) & Filter.Eq(a => a.Revision, revision) & Filter.Gt(a => a.ExpiresAt, now);
        var update = role == AgreementRoles.Buyer
            ? Builders<AgreementDocument>.Update.Set(a => a.BuyerConfirmedAt, now)
            : Builders<AgreementDocument>.Update.Set(a => a.SellerConfirmedAt, now);
        var after = await agreements.FindOneAndUpdateAsync(
            current,
            update.Set(a => a.UpdatedAt, now),
            new FindOneAndUpdateOptions<AgreementDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
        if (after is not { BuyerConfirmedAt: not null, SellerConfirmedAt: not null } || after.Status == AgreementStatuses.Confirmed)
        {
            return after;
        }

        // Los dos sobre la misma version: si en medio alguien edito, la version ya no coincide y no se confirma.
        return await agreements.FindOneAndUpdateAsync(
            current & Filter.Ne(a => a.BuyerConfirmedAt, null) & Filter.Ne(a => a.SellerConfirmedAt, null),
            Builders<AgreementDocument>.Update.Set(a => a.Status, AgreementStatuses.Confirmed),
            new FindOneAndUpdateOptions<AgreementDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false) ?? after;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            && (await agreements.DeleteOneAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false)).DeletedCount > 0;

    /// <inheritdoc />
    public async Task<long> DeleteByParticipantAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await agreements.DeleteManyAsync(Filter.Eq(a => a.BuyerId, userId) | Filter.Eq(a => a.SellerId, userId), cancellationToken)
                .ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Acuerdos en memoria (Development y pruebas).</summary>
internal sealed class InMemoryAgreementRepository : IAgreementRepository
{
    private readonly Lock gate = new();
    private readonly List<AgreementDocument> agreements = [];

    /// <inheritdoc />
    public Task<AgreementDocument> ReplaceForConversationAsync(AgreementDocument agreement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        lock (gate)
        {
            agreements.RemoveAll(a => a.ConversationId == agreement.ConversationId);
            agreements.Add(agreement);
            return Task.FromResult(agreement);
        }
    }

    /// <inheritdoc />
    public Task<AgreementDocument?> FindByIdAsync(string id, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(agreements.FirstOrDefault(a => a.Id == id && a.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<AgreementDocument?> FindByConversationAsync(string conversationId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(agreements.FirstOrDefault(a => a.ConversationId == conversationId && a.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<AgreementDocument?> UpdateProposalAsync(string id, int revision, AgreementProposal proposal, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var i = agreements.FindIndex(a => a.Id == id && a.Revision == revision && a.ExpiresAt > now);
            if (i < 0)
            {
                return Task.FromResult<AgreementDocument?>(null);
            }

            var a = agreements[i];
            agreements[i] = Copy(a, proposal, a.Revision + 1, AgreementStatuses.Proposed, null, null, now);
            return Task.FromResult<AgreementDocument?>(agreements[i]);
        }
    }

    /// <inheritdoc />
    public Task<AgreementDocument?> ConfirmAsync(string id, int revision, string role, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var i = agreements.FindIndex(a => a.Id == id && a.Revision == revision && a.ExpiresAt > now);
            if (i < 0)
            {
                return Task.FromResult<AgreementDocument?>(null);
            }

            var a = agreements[i];
            var buyer = role == AgreementRoles.Buyer ? now : a.BuyerConfirmedAt;
            var seller = role == AgreementRoles.Seller ? now : a.SellerConfirmedAt;
            var status = buyer is not null && seller is not null ? AgreementStatuses.Confirmed : a.Status;
            agreements[i] = Copy(a, a.Proposal, a.Revision, status, buyer, seller, now);
            return Task.FromResult<AgreementDocument?>(agreements[i]);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(agreements.RemoveAll(a => a.Id == id) > 0);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByParticipantAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)agreements.RemoveAll(a => a.BuyerId == userId || a.SellerId == userId));
        }
    }

    private static AgreementDocument Copy(
        AgreementDocument a,
        AgreementProposal proposal,
        int revision,
        string status,
        DateTime? buyerConfirmedAt,
        DateTime? sellerConfirmedAt,
        DateTime now) => new()
        {
            Id = a.Id,
            CommunityId = a.CommunityId,
            ConversationId = a.ConversationId,
            BuyerId = a.BuyerId,
            SellerId = a.SellerId,
            RequestedBy = a.RequestedBy,
            Proposal = proposal,
            NotIdentified = a.NotIdentified,
            Revision = revision,
            Status = status,
            BuyerConfirmedAt = buyerConfirmedAt,
            SellerConfirmedAt = sellerConfirmedAt,
            CreatedAt = a.CreatedAt,
            UpdatedAt = now,
            ExpiresAt = a.ExpiresAt,
            SchemaVersion = a.SchemaVersion,
        };
}
