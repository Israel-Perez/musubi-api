// 019-pedidos-estados (HU-24): aceptar, rechazar, cancelar y completar pedidos. El pedido guarda quien confirmo la
// entrega (handover, RN-19) y quien cancelo y por que (cancellation, RN-20). El centro de notificaciones acepta los
// avisos nuevos de los pedidos.
// Idempotente: se puede correr N veces despues de 018-preferencias.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '019-pedidos-estados';
const SCHEMA_VERSION = 19;

const id = { bsonType: 'objectId' };
const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };
const status = { enum: ['pending', 'confirmed', 'completed', 'cancelled'] };
// La lista cerrada de motivos (HU-24); account_deleted lo pone el sistema (HU-46).
const reason = { enum: ['changed_mind', 'unavailable', 'no_agreement', 'schedule', 'no_show', 'other', 'account_deleted'] };

function setValidator(collection, validator) {
  db.runCommand({ collMod: collection, validator: validator, validationLevel: 'strict', validationAction: 'error' });
}

// Misma forma que 014-pedidos (collMod reemplaza el validador completo), con handover y cancellation.
setValidator('orders', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'number', 'buyerId', 'sellerId', 'buyerName', 'sellerName', 'conversationId',
      'item', 'delivery', 'status', 'history', 'createdAt', 'updatedAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      number: { bsonType: 'int', minimum: 1 },
      buyerId: id,
      sellerId: id,
      buyerName: name,
      sellerName: name,
      conversationId: id,
      item: {
        bsonType: 'object',
        required: ['source', 'sourceId', 'title', 'priceCents', 'quantity', 'totalCents'],
        properties: {
          source: { enum: ['product', 'offer'] },
          sourceId: id,
          productId: id,
          title: { bsonType: 'string', minLength: 1, maxLength: 80 },
          photo: { bsonType: 'string', pattern: '^/media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
          priceCents: { bsonType: ['long', 'int'], minimum: 1, maximum: 99999900 },
          quantity: { bsonType: 'int', minimum: 1, maximum: 10 },
          totalCents: { bsonType: ['long', 'int'], minimum: 1 },
        },
      },
      delivery: {
        bsonType: 'object',
        required: ['pointSlug', 'pointName', 'at'],
        properties: {
          pointSlug: slug,
          pointName: { bsonType: 'string', minLength: 1, maxLength: 60 },
          detail: { bsonType: 'string', minLength: 1, maxLength: 120 },
          at: date,
        },
      },
      payment: {
        bsonType: 'object',
        required: ['method', 'chosenAt'],
        properties: {
          method: { enum: ['cash', 'transfer'] },
          chosenAt: date,
        },
      },
      // RN-19: cada parte confirma su lado; con las dos fechas el pedido se completa.
      handover: {
        bsonType: 'object',
        properties: { buyerAt: date, sellerAt: date },
      },
      // RN-20: quien cancelo (o el sistema, HU-46) y el motivo de la lista.
      cancellation: {
        bsonType: 'object',
        required: ['by', 'reason', 'at'],
        properties: {
          by: { enum: ['buyer', 'seller', 'system'] },
          reason: reason,
          at: date,
        },
      },
      status: status,
      history: {
        bsonType: 'array', minItems: 1, maxItems: 10,
        items: {
          bsonType: 'object',
          required: ['status', 'at'],
          properties: { status: status, at: date, reason: { bsonType: 'string', maxLength: 40 } },
        },
      },
      createdAt: date,
      updatedAt: date,
      schemaVersion: schemaVersion,
    },
  },
});

// Misma forma que 017-avisos, con los cuatro tipos de los pedidos (HU-24).
setValidator('notifications', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'type', 'category', 'title', 'body', 'target', 'createdAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      type: {
        enum: ['order_created', 'order_confirmed', 'order_cancelled', 'order_handover', 'order_completed',
          'agreement_confirmed', 'offer_received', 'offer_accepted', 'offer_rejected', 'request_expiring',
          'request_expired'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted'] },
          id: id,
        },
      },
      dedupeKey: { bsonType: 'string', minLength: 1, maxLength: 120 },
      readAt: date,
      createdAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
    },
  },
});

const meta = db.getCollection('_meta');
meta.updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);

print(SCRIPT_ID + ': OK (schema version ' + meta.findOne({ _id: 'schema' }).version + ')');
