// 035-coincidencias-semanticas (HU-60): matchEmbeddings guarda el vector (embedding) del texto publico de cada
// publicacion y cada solicitud "Se busca" para las coincidencias por significado. textHash dice de que texto y modelo
// salio (si cambia, se recalcula). ownerId borra los de una cuenta (HU-46). Sin TTL: un vector viejo solo se ignora.
// Idempotente: se puede correr N veces despues de 034-chat-fotos.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '035-coincidencias-semanticas';
const SCHEMA_VERSION = 35;

function ensureCollection(name, validator) {
  const options = { validator: validator, validationLevel: 'strict', validationAction: 'error' };
  if (db.getCollectionNames().includes(name)) {
    db.runCommand(Object.assign({ collMod: name }, options));
  } else {
    db.createCollection(name, options);
  }
}

const id = { bsonType: 'objectId' };

ensureCollection('matchEmbeddings', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'kind', 'itemId', 'communityId', 'ownerId', 'textHash', 'vector', 'updatedAt', 'schemaVersion'],
    properties: {
      // {kind}:{id}
      _id: { bsonType: 'string', pattern: '^(product|wanted):[0-9a-f]{24}$' },
      kind: { enum: ['product', 'wanted'] },
      itemId: id,
      communityId: id,
      ownerId: id,
      textHash: { bsonType: 'string', pattern: '^[0-9a-f]{32}$' },
      // 256 dimensiones con Gemini; hasta 3072 si se cambia el modelo.
      vector: { bsonType: 'array', minItems: 1, maxItems: 3072, items: { bsonType: 'double' } },
      updatedAt: { bsonType: 'date' },
      schemaVersion: { bsonType: 'int', minimum: 1 },
    },
  },
});

// Borrar los vectores de una cuenta (HU-46).
db.matchEmbeddings.createIndex({ ownerId: 1 }, { name: 'ix_owner' });

db.getCollection('_meta').updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);
