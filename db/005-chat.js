// 005-chat (HU-20): conversaciones entre comprador y vendedor y sus mensajes de texto.
// Idempotente: se puede correr N veces despues de 004-perfiles.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '005-chat';
const SCHEMA_VERSION = 5;

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

// conversations: una por comprador y elemento (RN-21). Los dos participantes, el contexto (copia del
// nombre, R8) y la copia del ultimo mensaje para la bandeja (HU-22).
ensureCollection('conversations', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'buyerId', 'sellerId', 'participants', 'context', 'lastMessageAt', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      buyerId: id,
      sellerId: id,
      participants: { bsonType: 'array', minItems: 2, maxItems: 2, uniqueItems: true, items: id },
      context: {
        bsonType: 'object',
        required: ['type', 'id', 'title'],
        properties: {
          // HU-20: publicaciones. Solicitudes y ofertas (HU-15/16) se agregan aqui en Sprint 3.
          type: { enum: ['product'] },
          id: id,
          title: { bsonType: 'string', minLength: 1, maxLength: 80 },
        },
      },
      // Se omite mientras no haya mensajes (la API no guarda nulos).
      lastMessage: {
        bsonType: 'object',
        required: ['senderId', 'text', 'at'],
        properties: {
          senderId: id,
          text: { bsonType: 'string', minLength: 1, maxLength: 120 },
          at: date,
        },
      },
      lastMessageAt: date,
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Dos toques seguidos en "Mensaje" no crean dos conversaciones.
db.conversations.createIndex({ 'context.type': 1, 'context.id': 1, buyerId: 1 }, { name: 'ux_context_buyer', unique: true });
// Bandeja (HU-22): mis conversaciones por actividad reciente. Tambien la usa el borrado de cuenta (HU-46).
db.conversations.createIndex({ participants: 1, lastMessageAt: -1 }, { name: 'ix_participant_lastAt' });

// messages: texto moderado (02-07) de 1 a 1000 caracteres.
ensureCollection('messages', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'conversationId', 'communityId', 'senderId', 'text', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      conversationId: id,
      communityId: id,
      senderId: id,
      text: { bsonType: 'string', minLength: 1, maxLength: 1000 },
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Pagina por cursor (_id < ultimo) de una conversacion, de la mas nueva a la mas vieja (R2).
db.messages.createIndex({ conversationId: 1, _id: -1 }, { name: 'ix_conversation_id' });

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
