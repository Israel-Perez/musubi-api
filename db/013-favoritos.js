// 013-favoritos (HU-53): productos guardados por una persona. Un favorito por usuario y publicacion.
// Idempotente: se puede correr N veces despues de 012-ofertas-chat.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '013-favoritos';
const SCHEMA_VERSION = 13;

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

// favorites (HU-53): quien guardo, que publicacion y de que campus. El titulo, el precio y la foto se leen
// de products; aqui no se copian (una tabla viva, R8).
ensureCollection('favorites', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'productId', 'communityId', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      productId: id,
      communityId: id,
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Dos toques en el corazon no duplican; resuelve "mis favoritos".
db.favorites.createIndex({ userId: 1, productId: 1 }, { name: 'ux_user_product', unique: true });
// La lista "Favoritos" del mas reciente al mas viejo.
db.favorites.createIndex({ userId: 1, createdAt: -1 }, { name: 'ix_user_id' });

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
