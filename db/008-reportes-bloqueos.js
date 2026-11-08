// 008-reportes-bloqueos (HU-28, HU-57): reportes para la revision del equipo y bloqueos entre personas.
// Idempotente: se puede correr N veces despues de 007-busqueda.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '008-reportes-bloqueos';
const SCHEMA_VERSION = 8;

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

// reports (HU-28): quien reporta, que (una persona, una publicacion o un problema general), el motivo de la
// maqueta y "Cuentanos que paso". Los revisa el equipo (HU-39); status 'open' al crearse.
ensureCollection('reports', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'reporterId', 'target', 'reason', 'status', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      reporterId: id,
      target: {
        bsonType: 'object',
        required: ['type'],
        properties: {
          type: { enum: ['general', 'user', 'product'] },
          // Se omite en 'general'.
          id: id,
        },
      },
      // Conversacion donde paso, si se reporto desde el chat.
      conversationId: id,
      reason: { enum: ['payment', 'not_as_described', 'no_show', 'off_platform', 'other'] },
      details: { bsonType: 'string', minLength: 1, maxLength: 1000 },
      // HU-39 agrega los estados de la revision.
      status: { enum: ['open'] },
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Bandeja del equipo (HU-39): los abiertos de un campus, del mas nuevo al mas viejo.
db.reports.createIndex({ communityId: 1, status: 1, createdAt: -1 }, { name: 'ix_community_status' });
// Borrado de cuenta (HU-46): los reportes que hizo una persona.
db.reports.createIndex({ reporterId: 1 }, { name: 'ix_reporter' });

// blocks (HU-57): uno por pareja. Con un bloqueo en cualquier direccion no se escriben ni ven publicaciones
// o perfil del otro. blockedName es copia del nombre (R8) para "Usuarios bloqueados".
ensureCollection('blocks', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'blockerId', 'blockedId', 'blockedName', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      blockerId: id,
      blockedId: id,
      blockedName: { bsonType: 'string', minLength: 1, maxLength: 120 },
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Dos toques en "Bloquear" no duplican; tambien resuelve "a quienes bloquee" y el primer lado de "hay bloqueo".
db.blocks.createIndex({ blockerId: 1, blockedId: 1 }, { name: 'ux_blocker_blocked', unique: true });
// El otro lado: quienes me bloquearon (vitrina, chat y perfil).
db.blocks.createIndex({ blockedId: 1 }, { name: 'ix_blocked' });

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
