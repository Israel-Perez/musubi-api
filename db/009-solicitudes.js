// 009-solicitudes (HU-13): solicitudes de compra "Se busca" (lo que alguien necesita, con presupuesto).
// Idempotente: se puede correr N veces despues de 008-reportes-bloqueos.js.
// Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '009-solicitudes';
const SCHEMA_VERSION = 9;

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

// wantedRequests (HU-13): RN-09 obligatorios (producto, categoria, presupuesto y cantidad). El estado vencido
// no se guarda: se calcula con expiresAt (RN-10). HU-14 agrega closed y cancelled al cerrar o cancelar.
ensureCollection('wantedRequests', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'requesterId', 'requesterName', 'title', 'categorySlug', 'categoryName', 'features', 'budgetCents', 'quantity', 'expiresAt', 'status', 'offersCount', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      requesterId: id,
      // Copia del nombre corto ("Ana R.", R8).
      requesterName: { bsonType: 'string', minLength: 1, maxLength: 60 },
      title: { bsonType: 'string', minLength: 3, maxLength: 80 },
      // Slug de una categoria del campus, u "otro".
      categorySlug: { bsonType: 'string', minLength: 1, maxLength: 40 },
      categoryName: { bsonType: 'string', minLength: 1, maxLength: 60 },
      description: { bsonType: 'string', minLength: 1, maxLength: 300 },
      features: { bsonType: 'array', maxItems: 5, items: { bsonType: 'string', minLength: 1, maxLength: 40 } },
      budgetCents: { bsonType: 'long', minimum: 100, maximum: 99999900 },
      quantity: { bsonType: 'int', minimum: 1, maximum: 10 },
      neededBy: date,
      expiresAt: date,
      status: { enum: ['active', 'closed', 'cancelled'] },
      offersCount: { bsonType: 'int', minimum: 0 },
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Tablero "Se busca" del campus (HU-15): las activas, de la mas nueva a la mas vieja.
db.wantedRequests.createIndex({ communityId: 1, status: 1, _id: -1 }, { name: 'ix_community_status_id' });
// "Mis solicitudes", el maximo de activas (RN-12) y el borrado de cuenta (HU-46).
db.wantedRequests.createIndex({ requesterId: 1, _id: -1 }, { name: 'ix_requester_id' });

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
