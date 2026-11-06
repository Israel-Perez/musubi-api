// 006-chat-bandeja (HU-22): no leidos por lado y copia de los nombres en conversations.
// Idempotente: se puede correr N veces despues de 005-chat.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '006-chat-bandeja';
const SCHEMA_VERSION = 6;

const id = { bsonType: 'objectId' };
const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const unread = { bsonType: 'int', minimum: 0 };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };

// Misma forma que 005-chat mas buyerName, sellerName (copias R8: el nombre no se edita) y
// buyerUnread, sellerUnread (HU-22). Son opcionales: las conversaciones creadas antes valen 0 y
// la API busca el nombre si no esta copiado.
db.runCommand({
  collMod: 'conversations',
  validator: {
    $jsonSchema: {
      bsonType: 'object',
      required: ['_id', 'communityId', 'buyerId', 'sellerId', 'participants', 'context', 'lastMessageAt', 'createdAt', 'schemaVersion'],
      properties: {
        _id: id,
        communityId: id,
        buyerId: id,
        sellerId: id,
        participants: { bsonType: 'array', minItems: 2, maxItems: 2, uniqueItems: true, items: id },
        buyerName: name,
        sellerName: name,
        context: {
          bsonType: 'object',
          required: ['type', 'id', 'title'],
          properties: {
            type: { enum: ['product'] },
            id: id,
            title: { bsonType: 'string', minLength: 1, maxLength: 80 },
          },
        },
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
        buyerUnread: unread,
        sellerUnread: unread,
        createdAt: date,
        schemaVersion: schemaVersion,
      },
    },
  },
  validationLevel: 'strict',
  validationAction: 'error',
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
