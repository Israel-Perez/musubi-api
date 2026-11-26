// 036-indicadores (HU-42): los indicadores de uso del administrador. aiUsage anota cada "Resumir acuerdo" y cada
// acuerdo confirmado (con o sin correcciones), porque el acuerdo se borra al crear el pedido (RN-44); no guarda
// personas, conversaciones ni textos, y se borra solo al ano. notifications suma ix_type_created para contar los
// avisos de un tipo en un periodo (busquedas guardadas atendidas y "sin venderse").
// Idempotente: se puede correr N veces despues de 035-coincidencias-semanticas.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '036-indicadores';
const SCHEMA_VERSION = 36;

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

ensureCollection('aiUsage', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'kind', 'corrected', 'at', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      kind: { enum: ['summary', 'confirmed'] },
      corrected: { bsonType: 'bool' },
      at: date,
      expiresAt: date,
      schemaVersion: { bsonType: 'int', minimum: 1 },
    },
  },
});

db.aiUsage.createIndex({ communityId: 1, at: 1 }, { name: 'ix_community_at' });
db.aiUsage.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// Contar los avisos de un tipo en un periodo (HU-42).
db.notifications.createIndex({ type: 1, createdAt: 1 }, { name: 'ix_type_created' });

db.getCollection('_meta').updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);
