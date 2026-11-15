// 028-apartados (HU-52): apartar un producto 24 h y verificar la entrega con QR o codigo de 6 digitos. reservations
// guarda cada apartado con su codigo (solo lo ve quien aparta), sus intentos y el pedido completado que nace al
// entregar. ux_buyer_active deja uno activo por persona (decision de la usuaria del 15-Nov-2026) e ix_status_expires
// da al proceso periodico los que vencieron. El centro de avisos acepta reservation_created, reservation_cancelled,
// reservation_expired y el destino reservation.
// Idempotente: se puede correr N veces despues de 027-pagos.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '028-apartados';
const SCHEMA_VERSION = 28;

function ensureCollection(name, validator) {
  const options = { validator: validator, validationLevel: 'strict', validationAction: 'error' };
  if (db.getCollectionNames().includes(name)) {
    db.runCommand(Object.assign({ collMod: name }, options));
  } else {
    db.createCollection(name, options);
  }
}

const id = { bsonType: 'objectId' };
const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };

ensureCollection('reservations', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'number', 'buyerId', 'sellerId', 'buyerName', 'sellerName', 'conversationId',
      'productId', 'title', 'priceCents', 'pointSlug', 'pointName', 'pickupAt', 'code', 'status', 'createdAt',
      'expiresAt', 'updatedAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      number: { bsonType: 'int', minimum: 1 },
      buyerId: id,
      sellerId: id,
      buyerName: name,
      sellerName: name,
      conversationId: id,
      productId: id,
      title: { bsonType: 'string', minLength: 1, maxLength: 80 },
      photo: { bsonType: 'string', pattern: '^/media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
      priceCents: { bsonType: ['long', 'int'], minimum: 1, maximum: 99999900 },
      pointSlug: slug,
      pointName: { bsonType: 'string', minLength: 1, maxLength: 60 },
      pickupAt: date,
      code: { bsonType: 'string', pattern: '^[0-9]{6}$' },
      codeAttempts: { bsonType: 'int', minimum: 0, maximum: 5 },
      verifiedAt: date,
      orderId: id,
      status: { enum: ['active', 'collected', 'cancelled', 'expired'] },
      cancelledBy: { enum: ['buyer', 'seller', 'system'] },
      createdAt: date,
      expiresAt: date,
      updatedAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Uno activo por persona (1 a la vez): el indice unico parcial gana entre dos toques a la vez.
db.reservations.createIndex({ buyerId: 1 }, { name: 'ux_buyer_active', unique: true, partialFilterExpression: { status: 'active' } });
// "Yo aparte" y "Me apartaron", del mas nuevo al mas viejo.
db.reservations.createIndex({ buyerId: 1, _id: -1 }, { name: 'ix_buyer_id' });
db.reservations.createIndex({ sellerId: 1, _id: -1 }, { name: 'ix_seller_id' });
// Los que vencieron a las 24 h (proceso periodico).
db.reservations.createIndex({ status: 1, expiresAt: 1 }, { name: 'ix_status_expires' });

// Misma forma que 027-pagos, con los avisos de los apartados.
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
          'transfer_reported', 'transfer_not_received', 'transfer_reminder',
          'reservation_created', 'reservation_cancelled', 'reservation_expired'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted', 'wanted_board', 'none', 'product', 'my_products', 'reservation'] },
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
