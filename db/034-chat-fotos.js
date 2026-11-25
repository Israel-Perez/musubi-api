// 034-chat-fotos (HU-65): fotos, "escribiendo…" y "leido" en el chat. messages admite kind "photo" con la llave de
// la foto (chat/{conversacion}/{id}.webp, fuera de media/). conversations guarda cuando abrio cada lado
// (buyerReadAt y sellerReadAt). "Escribiendo…" no se guarda. Sin indices nuevos.
// Idempotente: se puede correr N veces despues de 033-precios.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '034-chat-fotos';
const SCHEMA_VERSION = 34;

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
const unread = { bsonType: 'int', minimum: 0 };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };

// Igual que en 012-ofertas-chat, con "Leido".
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
            type: { enum: ['product', 'offer'] },
            id: id,
            title: { bsonType: 'string', minLength: 1, maxLength: 80 },
            // Copia de la portada (o de la foto de la oferta) para la bandeja; ruta /media/... del bucket.
            photo: { bsonType: 'string', pattern: '^/media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
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
        // HU-65: cuando abrio cada lado la conversacion ("Leido").
        buyerReadAt: date,
        sellerReadAt: date,
        createdAt: date,
        schemaVersion: schemaVersion,
      },
    },
  },
  validationLevel: 'strict',
  validationAction: 'error',
});

// Igual que en 015-acuerdos, con las fotos.
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
      // HU-65: una foto; su llave vive fuera de media/ (CloudFront no la sirve, solo la API a los participantes).
      kind: { enum: ['notice', 'photo'] },
      photo: { bsonType: 'string', pattern: '^chat/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
      createdAt: date,
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
