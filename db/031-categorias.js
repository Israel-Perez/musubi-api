// 031-categorias (HU-41): el administrador agrega, renombra, desactiva y vuelve a activar las categorias de su campus.
// categories no cambia: desde 002-publicaciones.js tiene nombre (2 a 40), orden, active y el slug unico por campus
// (ux_community_slug); el slug no cambia al renombrar porque lo guardan los productos y las solicitudes. moderationLog
// acepta las acciones de categorias (targetType category, targetId = la categoria).
// Idempotente: se puede correr N veces despues de 030-moderacion.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '031-categorias';
const SCHEMA_VERSION = 31;

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
const reason500 = { bsonType: 'string', minLength: 1, maxLength: 500 };

// moderationLog (RNF-04): igual que en 030, con los cambios de categorias.
ensureCollection('moderationLog', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'actorId', 'action', 'targetType', 'at', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      actorId: id,
      action: {
        enum: ['report_dismissed', 'report_upheld', 'product_removed', 'account_suspended', 'account_reactivated',
          // HU-41
          'category_created', 'category_renamed', 'category_deactivated', 'category_reactivated'],
      },
      targetType: { enum: ['general', 'user', 'product', 'category'] },
      targetId: id,
      subjectId: id,
      reportId: id,
      reason: reason500,
      at: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Sus indices (ix_subject_at, ix_actor_at) siguen los de 030.

db.getCollection('_meta').updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);
