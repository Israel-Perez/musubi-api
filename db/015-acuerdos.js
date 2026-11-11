// 015-acuerdos (HU-32/33/34): el acuerdo con IA de una conversacion y los avisos dentro del chat.
// Idempotente: se puede correr N veces despues de 014-pedidos.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '015-acuerdos';
const SCHEMA_VERSION = 15;

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

// agreements: la propuesta de la IA corregida y confirmada por los dos (RN-41). Del chat no se guarda nada; el
// acuerdo se borra al crear el pedido, al descartarlo o a los 30 dias (ttl_expires_at, RN-44).
ensureCollection('agreements', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'conversationId', 'buyerId', 'sellerId', 'requestedBy', 'proposal',
      'notIdentified', 'revision', 'status', 'createdAt', 'updatedAt', 'expiresAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      conversationId: id,
      buyerId: id,
      sellerId: id,
      requestedBy: id,
      proposal: {
        bsonType: 'object',
        required: ['title'],
        properties: {
          title: { bsonType: 'string', minLength: 1, maxLength: 80 },
          priceCents: { bsonType: ['long', 'int'], minimum: 100, maximum: 99999900 },
          quantity: { bsonType: 'int', minimum: 1, maximum: 10 },
          pointSlug: { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' },
          pointName: { bsonType: 'string', minLength: 1, maxLength: 120 },
          detail: { bsonType: 'string', minLength: 1, maxLength: 120 },
          at: date,
          conditions: { bsonType: 'string', minLength: 1, maxLength: 200 },
        },
      },
      notIdentified: {
        bsonType: 'array',
        maxItems: 5,
        items: { enum: ['title', 'priceCents', 'quantity', 'meetingPoint', 'deliveryAt'] },
      },
      revision: { bsonType: 'int', minimum: 1 },
      status: { enum: ['proposed', 'confirmed'] },
      buyerConfirmedAt: date,
      sellerConfirmedAt: date,
      createdAt: date,
      updatedAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// Uno por conversacion: pedir otro resumen reemplaza al anterior.
db.agreements.createIndex({ conversationId: 1 }, { name: 'ux_conversation', unique: true });
// Borrado de una cuenta (HU-46).
db.agreements.createIndex({ buyerId: 1 }, { name: 'ix_buyer_id' });
db.agreements.createIndex({ sellerId: 1 }, { name: 'ix_seller_id' });
// RN-44: MongoDB lo borra solo al llegar expiresAt.
db.agreements.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// messages: los avisos de la app dentro del chat ("Ana pidio un resumen con IA", RN-40) llevan kind "notice"; un
// mensaje de texto no lleva kind. Mismo esquema de 005-chat mas ese campo.
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
      kind: { enum: ['notice'] },
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
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
