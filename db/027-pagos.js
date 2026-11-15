// 027-pagos (HU-55, D-01): pagar por transferencia a la cuenta de quien vende o en efectivo con codigo de entrega.
// payoutAccounts guarda la cuenta para recibir pagos de cada persona (_id = su id) con la CLABE cifrada (AES-GCM, R10)
// y solo sus ultimos 4 digitos en claro. orders.payment suma la referencia de la transferencia, cuando se aviso, se
// valido o no llego, y el codigo de entrega del efectivo con sus intentos. ix_transfer_reported da el recordatorio de
// las 24 h. El centro de avisos acepta transfer_reported, transfer_not_received y transfer_reminder.
// Idempotente: se puede correr N veces despues de 026-apoyo-vendedores.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '027-pagos';
const SCHEMA_VERSION = 27;

function ensureCollection(name, validator) {
  const options = { validator: validator, validationLevel: 'strict', validationAction: 'error' };
  if (db.getCollectionNames().includes(name)) {
    db.runCommand(Object.assign({ collMod: name }, options));
  } else {
    db.createCollection(name, options);
  }
}

function setValidator(collection, validator) {
  db.runCommand({ collMod: collection, validator: validator, validationLevel: 'strict', validationAction: 'error' });
}

const id = { bsonType: 'objectId' };
const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };
const status = { enum: ['pending', 'confirmed', 'completed', 'cancelled'] };
const reason = { enum: ['changed_mind', 'unavailable', 'no_agreement', 'schedule', 'no_show', 'other', 'account_deleted'] };

// payoutAccounts (Cuenta-pagos): una por persona; nunca la CLABE en claro.
ensureCollection('payoutAccounts', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'holderName', 'bankCode', 'bankName', 'clabeCiphertext', 'clabeLast4', 'verifiedAt',
      'createdAt', 'updatedAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      holderName: { bsonType: 'string', minLength: 3, maxLength: 80 },
      bankCode: { bsonType: 'string', pattern: '^[0-9]{3}$' },
      bankName: { bsonType: 'string', minLength: 1, maxLength: 40 },
      clabeCiphertext: { bsonType: 'string', pattern: '^v1:[A-Za-z0-9+/=]{40,200}$' },
      clabeLast4: { bsonType: 'string', pattern: '^[0-9]{4}$' },
      verifiedAt: date,
      createdAt: date,
      updatedAt: date,
      schemaVersion: schemaVersion,
    },
  },
});

// "Confirma la transferencia de Ana": pendientes por transferencia, del aviso mas viejo al mas nuevo.
db.orders.createIndex({ status: 1, 'payment.method': 1, 'payment.reportedAt': 1 }, { name: 'ix_transfer_reported' });

// Misma forma que 020-calificaciones (collMod reemplaza el validador completo), con el pago de HU-55.
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
      // HU-55: la transferencia (referencia, aviso, "todavia no me llega" y validacion) y el codigo de entrega del efectivo.
      payment: {
        bsonType: 'object',
        required: ['method', 'chosenAt'],
        properties: {
          method: { enum: ['cash', 'transfer'] },
          chosenAt: date,
          reference: { bsonType: 'string', pattern: '^[A-Z0-9-]{4,30}$' },
          reportedAt: date,
          notReceivedAt: date,
          validatedAt: date,
          deliveryCode: { bsonType: 'string', pattern: '^[0-9]{4}$' },
          codeAttempts: { bsonType: 'int', minimum: 0, maximum: 5 },
        },
      },
      // RN-19: cada parte confirma su lado; con las dos fechas el pedido se completa.
      handover: {
        bsonType: 'object',
        properties: { buyerAt: date, sellerAt: date },
      },
      // RN-20: quien cancelo (o el sistema, HU-46) y el motivo de la lista.
      // HU-27/HU-45: cuando califico cada parte (una vez por pedido, RN-24).
      ratings: {
        bsonType: 'object',
        properties: { buyerAt: date, sellerAt: date },
      },
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

// Misma forma que 026-apoyo-vendedores, con los avisos de la transferencia.
ensureCollection('notifications', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'type', 'category', 'title', 'body', 'target', 'createdAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      type: {
        enum: ['order_created', 'order_confirmed', 'order_cancelled', 'order_handover', 'order_completed',
          'agreement_confirmed', 'offer_received', 'offer_accepted', 'offer_rejected', 'request_expiring',
          'request_expired', 'wanted_match', 'offer_closed', 'product_match', 'saved_search_match', 'slow_listing',
          'transfer_reported', 'transfer_not_received', 'transfer_reminder'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted', 'wanted_board', 'none', 'product', 'my_products'] },
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

db.getCollection('_meta').updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);
