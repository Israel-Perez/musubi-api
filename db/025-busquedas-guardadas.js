// 025-busquedas-guardadas (HU-12): "Avísame cuando aparezca". Una busqueda sin resultados, con sus filtros, privada
// (RNF-07), una por persona y busqueda (`key`) y que vence sola a los 30 dias (RN-34, decision de la usuaria del
// 14-Nov-2026). El validador de `notifications` admite `saved_search_match`. Idempotente: se puede correr N veces
// despues de 024-coincidencias.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '025-busquedas-guardadas';
const SCHEMA_VERSION = 25;

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
const price = { bsonType: ['long', 'int', 'null'], minimum: 0 };

ensureCollection('savedSearches', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'communityId', 'query', 'terms', 'categories', 'key', 'createdAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      communityId: id,
      query: { bsonType: 'string', minLength: 1, maxLength: 80 },
      terms: { bsonType: 'array', minItems: 1, maxItems: 5, items: { bsonType: 'string', minLength: 1 } },
      categories: { bsonType: 'array', maxItems: 10, items: { bsonType: 'string', minLength: 1 } },
      minPriceCents: price,
      maxPriceCents: price,
      condition: { enum: ['new', 'used', null] },
      key: { bsonType: 'string', minLength: 1 },
      createdAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// La misma busqueda guardada otra vez solo renueva su vencimiento.
db.savedSearches.createIndex({ userId: 1, key: 1 }, { name: 'ux_user_key', unique: true });
// "Mis alertas" y el limite de 10 por persona.
db.savedSearches.createIndex({ userId: 1, expiresAt: 1 }, { name: 'ix_user_expires' });
// Las vigentes del campus, al publicar (HU-29/HU-12).
db.savedSearches.createIndex({ communityId: 1, expiresAt: 1 }, { name: 'ix_community_expires' });
// Se borran solas a los 30 dias (RN-34).
db.savedSearches.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// Misma forma que 024-coincidencias (collMod reemplaza el validador completo), con el aviso de HU-12.
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
          'request_expired', 'wanted_match', 'offer_closed', 'product_match', 'saved_search_match'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted', 'wanted_board', 'none', 'product'] },
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
