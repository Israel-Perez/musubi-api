// 030-moderacion (HU-39, HU-40): la bandeja de reportes del administrador y la bitacora de moderacion (RNF-04).
// reports admite los estados de la revision (dismissed, upheld), la persona responsable (subjectId, para contar los
// reportes procedentes de una cuenta, RN-27) y quien lo reviso. moderationLog guarda cada accion de un
// administrador: descartar o confirmar un reporte, retirar una publicacion y suspender o reactivar una cuenta. users
// ya admitia el estado suspended y el rol admin desde 001-identidad.js. El centro de avisos acepta report_reviewed y
// product_removed (solo push).
// Idempotente: se puede correr N veces despues de 029-avisos-disponibilidad.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '030-moderacion';
const SCHEMA_VERSION = 30;

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

// reports (HU-28 + HU-39): igual que en 008, con la revision.
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
      conversationId: id,
      reason: { enum: ['payment', 'not_as_described', 'no_show', 'off_platform', 'other'] },
      details: { bsonType: 'string', minLength: 1, maxLength: 1000 },
      // open al crearse; dismissed (descartado) o upheld (procedente) al revisarse.
      status: { enum: ['open', 'dismissed', 'upheld'] },
      // La persona reportada o quien vende la publicacion; se omite en un problema general.
      subjectId: id,
      reviewedBy: id,
      reviewedAt: date,
      reviewNote: reason500,
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Reportes procedentes de una cuenta (RN-27): el contador de "Cuenta" y de la bandeja.
db.reports.createIndex({ subjectId: 1, status: 1 }, { name: 'ix_subject_status', partialFilterExpression: { subjectId: { $exists: true } } });

// moderationLog (RNF-04): una accion por documento; no se edita ni se borra (tampoco al borrar una cuenta).
ensureCollection('moderationLog', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'actorId', 'action', 'targetType', 'at', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      actorId: id,
      action: { enum: ['report_dismissed', 'report_upheld', 'product_removed', 'account_suspended', 'account_reactivated'] },
      targetType: { enum: ['general', 'user', 'product'] },
      targetId: id,
      subjectId: id,
      reportId: id,
      reason: reason500,
      at: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Historial de una cuenta en "Cuenta" (HU-40).
db.moderationLog.createIndex({ subjectId: 1, at: -1 }, { name: 'ix_subject_at', partialFilterExpression: { subjectId: { $exists: true } } });
// Lo que hizo cada administrador (auditoria).
db.moderationLog.createIndex({ actorId: 1, at: -1 }, { name: 'ix_actor_at' });

// notifications (HU-31): igual que en 029, con los avisos de la moderacion.
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
          'request_expired', 'wanted_match', 'offer_closed', 'product_match', 'saved_search_match', 'slow_listing',
          'transfer_reported', 'transfer_not_received', 'transfer_reminder',
          'reservation_created', 'reservation_cancelled', 'reservation_expired', 'back_in_stock',
          // HU-39: solo push (no tienen pestana), pero el validador acepta cada tipo del codigo.
          'report_reviewed', 'product_removed'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted', 'wanted_board', 'none', 'product', 'my_products', 'reservation'] },
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
