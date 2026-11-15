// 026-apoyo-vendedores (HU-36/37/38): "Llevan tiempo sin venderse". Sin colecciones nuevas: el aviso de las 3 semanas
// (`slow_listing`, que abre "Mis productos": destino `my_products`) entra al validador de `notifications`, y el indice
// `ix_status_created` le da al proceso periodico las publicaciones disponibles que cruzaron el umbral, de todos los
// campus. Idempotente: se puede correr N veces despues de 025-busquedas-guardadas.js. Catalogo: docs/modelo-datos.md.

const SCRIPT_ID = '026-apoyo-vendedores';
const SCHEMA_VERSION = 26;

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

// "Tu bata lleva 34 días sin venderse": disponibles por fecha de publicacion (HU-36, RN-35).
db.products.createIndex({ status: 1, createdAt: 1 }, { name: 'ix_status_created' });

// Misma forma que 025-busquedas-guardadas (collMod reemplaza el validador completo), con el aviso de HU-36.
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
          'request_expired', 'wanted_match', 'offer_closed', 'product_match', 'saved_search_match', 'slow_listing'],
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
