// 012-ofertas-chat (HU-17): elegir ofertas. Una conversacion tambien puede nacer de una oferta (context.type
// "offer") y una oferta puede quedar aceptada o rechazada.
// Idempotente: se puede correr N veces despues de 011-ofertas.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '012-ofertas-chat';
const SCHEMA_VERSION = 12;

const id = { bsonType: 'objectId' };
const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const unread = { bsonType: 'int', minimum: 0 };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };

// Misma forma que 006-chat-bandeja; context.type admite "offer" (HU-17: "Abrir chat" desde una oferta, con
// quien busca como comprador y quien ofrece como vendedor).
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
        createdAt: date,
        schemaVersion: schemaVersion,
      },
    },
  },
  validationLevel: 'strict',
  validationAction: 'error',
});

// Misma forma que 011-ofertas; status admite accepted y rejected (HU-17), decidedAt guarda cuando, y
// conditionDetail copia el detalle de la publicacion vinculada ("como nueva", R8).
db.runCommand({
  collMod: 'offers',
  validator: {
    $jsonSchema: {
      bsonType: 'object',
      required: ['_id', 'communityId', 'requestId', 'requesterId', 'sellerId', 'sellerName', 'kind', 'title', 'priceCents', 'condition', 'status', 'createdAt', 'updatedAt', 'schemaVersion'],
      properties: {
        _id: id,
        communityId: id,
        requestId: id,
        requesterId: id,
        sellerId: id,
        sellerName: { bsonType: 'string', minLength: 1, maxLength: 60 },
        kind: { enum: ['listing', 'direct'] },
        productId: id,
        title: { bsonType: 'string', minLength: 3, maxLength: 300 },
        priceCents: { bsonType: 'long', minimum: 100, maximum: 99999900 },
        condition: { enum: ['new', 'used'] },
        conditionDetail: { bsonType: 'string', minLength: 1, maxLength: 40 },
        photoKey: { bsonType: 'string', minLength: 1, maxLength: 200 },
        message: { bsonType: 'string', minLength: 1, maxLength: 300 },
        status: { enum: ['sent', 'accepted', 'rejected'] },
        decidedAt: date,
        createdAt: date,
        updatedAt: date,
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
