// 017-avisos (HU-31): el centro de notificaciones, los tokens de FCM de los dispositivos y el indice de las
// solicitudes por vencer. Idempotente: se puede correr N veces despues de 016-publicacion-vendida.js. Catalogo
// canonico: docs/modelo-datos.md.

const SCRIPT_ID = '017-avisos';
const SCHEMA_VERSION = 17;

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

// notifications: el historial de avisos (maqueta Notificaciones, RF-43). Los mensajes del chat no se guardan aqui
// (su historial es la bandeja). Se borran solos a los 60 dias.
ensureCollection('notifications', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'type', 'category', 'title', 'body', 'target', 'createdAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      type: {
        enum: ['order_created', 'agreement_confirmed', 'offer_received', 'offer_accepted', 'offer_rejected',
          'request_expiring', 'request_expired'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted'] },
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
// La pagina de "Todas" y la de cada pestana, del mas nuevo al mas viejo (cursor por _id).
db.notifications.createIndex({ userId: 1, _id: -1 }, { name: 'ix_user_id' });
db.notifications.createIndex({ userId: 1, category: 1, _id: -1 }, { name: 'ix_user_category_id' });
// Un mismo aviso una sola vez ("vence manana" aunque las dos instancias revisen a la vez).
db.notifications.createIndex(
  { userId: 1, dedupeKey: 1 },
  { name: 'ux_user_dedupe', unique: true, partialFilterExpression: { dedupeKey: { $exists: true } } },
);
db.notifications.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// deviceTokens: el token de FCM de cada dispositivo con sesion. Si la app no lo vuelve a registrar en 60 dias, se
// borra solo.
ensureCollection('deviceTokens', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'token', 'createdAt', 'lastSeenAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      userId: id,
      token: { bsonType: 'string', minLength: 1, maxLength: 512 },
      createdAt: date,
      lastSeenAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
db.deviceTokens.createIndex({ token: 1 }, { name: 'ux_token', unique: true });
db.deviceTokens.createIndex({ userId: 1 }, { name: 'ix_user_id' });
db.deviceTokens.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// wantedRequests: la revision de cada hora busca las activas que vencen en las proximas 24 horas o en las ultimas 24.
db.wantedRequests.createIndex({ status: 1, expiresAt: 1 }, { name: 'ix_status_expires' });

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
