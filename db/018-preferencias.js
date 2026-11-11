// 018-preferencias (HU-54): preferencia de apariencia por cuenta.
// Idempotente: se puede correr N veces despues de 017-avisos.js.

const SCRIPT_ID = '018-preferencias';
const SCHEMA_VERSION = 18;

function ensureCollection(name, validator) {
  const options = { validator: validator, validationLevel: 'strict', validationAction: 'error' };
  if (db.getCollectionNames().includes(name)) {
    db.runCommand(Object.assign({ collMod: name }, options));
  } else {
    db.createCollection(name, options);
  }
}

ensureCollection('preferences', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'theme', 'schemaVersion'],
    properties: {
      _id: { bsonType: 'objectId' },
      theme: { enum: ['system', 'light', 'dark'] },
      schemaVersion: { bsonType: 'int', minimum: 1 },
    },
  },
});
// _id = userId: Mongo ya crea el índice único para cada cuenta.

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
