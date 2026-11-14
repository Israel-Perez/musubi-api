// 022-interacciones (HU-35): lo que cada persona vio, guardo, le escribio al vendedor o compro, para "Recomendado para ti".
// Privado (RNF-07), una por persona, publicacion y tipo, y se borra solo a los 90 dias (decision de la usuaria,
// 13-Nov-2026). Idempotente: se puede correr N veces despues de 021-ventas-compras.js. Catalogo canonico:
// docs/modelo-datos.md.

const SCRIPT_ID = '022-interacciones';
const SCHEMA_VERSION = 22;

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

ensureCollection('interactions', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'communityId', 'productId', 'kind', 'count', 'at', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      communityId: id,
      productId: id,
      kind: { enum: ['view', 'favorite', 'chat', 'purchase'] },
      count: { bsonType: 'int', minimum: 1 },
      at: date,
      expiresAt: date,
      schemaVersion: { bsonType: 'int', minimum: 1 },
    },
  },
});
// Repetir una interaccion actualiza la misma (upsert) y dos peticiones al mismo tiempo no la duplican.
db.interactions.createIndex({ userId: 1, productId: 1, kind: 1 }, { name: 'ux_user_product_kind', unique: true });
// El perfil de una persona: sus interacciones mas recientes (y su borrado, HU-46).
db.interactions.createIndex({ userId: 1, at: -1 }, { name: 'ix_user_at' });
// El entrenamiento del modelo de un campus.
db.interactions.createIndex({ communityId: 1, at: -1 }, { name: 'ix_community_at' });
// Se borran solas a los 90 dias.
db.interactions.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// preferences (HU-54): se agrega "Recomendarte productos según tu actividad" (opcional; sin el campo es si).
ensureCollection('preferences', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'theme', 'schemaVersion'],
    properties: {
      _id: id,
      theme: { enum: ['system', 'light', 'dark'] },
      recommendations: { bsonType: 'bool' },
      schemaVersion: { bsonType: 'int', minimum: 1 },
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
