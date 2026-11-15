// 024-coincidencias (HU-18, HU-29, RF-25): cada coincidencia entre una solicitud y una publicacion, a quien se aviso
// y si termino en pedido (90 dias). Ademas, el validador de `notifications` admite los avisos de HU-19 (wanted_match,
// abre el tablero), HU-30 (offer_closed, no abre nada) y HU-29 (product_match, abre la publicacion): db/019 solo
// conocia los de los pedidos y las ofertas, y MongoDB rechazaba los demas. Idempotente: se puede correr N veces
// despues de 023-popularidad.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '024-coincidencias';
const SCHEMA_VERSION = 24;

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

ensureCollection('matches', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'requestId', 'productId', 'requesterId', 'sellerId', 'notifiedRequester',
      'notifiedSeller', 'createdAt', 'expiresAt', 'schemaVersion'],
    properties: {
      // `{requestId}:{productId}`: una por solicitud y publicacion.
      _id: { bsonType: 'string', minLength: 49, maxLength: 49 },
      communityId: id,
      requestId: id,
      productId: id,
      requesterId: id,
      sellerId: id,
      notifiedRequester: { bsonType: 'bool' },
      notifiedSeller: { bsonType: 'bool' },
      orderedAt: { bsonType: ['date', 'null'] },
      createdAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// "Termino en pedido": las de quien compra con esa publicacion.
db.matches.createIndex({ requesterId: 1, productId: 1 }, { name: 'ix_requester_product' });
// Las de una solicitud y el borrado de una cuenta (HU-46: como quien busca o como quien vende).
db.matches.createIndex({ requestId: 1 }, { name: 'ix_request' });
db.matches.createIndex({ sellerId: 1 }, { name: 'ix_seller' });
// Se borran solas a los 90 dias.
db.matches.createIndex({ expiresAt: 1 }, { name: 'ttl_expires_at', expireAfterSeconds: 0 });

// Misma forma que 019-pedidos-estados (collMod reemplaza el validador completo), con los avisos de HU-19, HU-29 y HU-30.
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
          'request_expired', 'wanted_match', 'offer_closed', 'product_match'],
      },
      category: { enum: ['orders', 'requests', 'matches'] },
      title: { bsonType: 'string', minLength: 1, maxLength: 120 },
      body: { bsonType: 'string', minLength: 1, maxLength: 140 },
      target: {
        bsonType: 'object',
        required: ['type', 'id'],
        properties: {
          type: { enum: ['conversation', 'order', 'wanted', 'wanted_board', 'none', 'product'] },
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
