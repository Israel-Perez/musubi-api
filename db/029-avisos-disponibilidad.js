// 029-avisos-disponibilidad (HU-51): "Avísame" en una publicación agotada. Un aviso por persona y publicación
// (`ux_user_product`), privado (RNF-07), que se borra al avisar (decisión de la usuaria del 16-Nov-2026: se usa una
// vez) o solo a los 90 días (TTL). El validador de `notifications` admite `back_in_stock`. Idempotente: se puede
// correr N veces después de 028-apartados.js. Catálogo canónico: docs/modelo-datos.md.

const SCRIPT_ID = '029-avisos-disponibilidad';
const SCHEMA_VERSION = 29;

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

ensureCollection('stockAlerts', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'communityId', 'productId', 'createdAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      communityId: id,
      productId: id,
      createdAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Pedir el aviso dos veces no lo duplica; también responde "¿ya lo tengo?".
db.stockAlerts.createIndex({ userId: 1, productId: 1 }, { name: 'ux_user_product', unique: true });
// Los avisos de una publicación cuando vuelve a tener piezas.
db.stockAlerts.createIndex({ productId: 1 }, { name: 'ix_product' });
// Se borran solos a los 90 días.
db.stockAlerts.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// Misma forma que 028-apartados (collMod reemplaza el validador completo), con el aviso de HU-51.
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
          'reservation_created', 'reservation_cancelled', 'reservation_expired', 'back_in_stock'],
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
