// 020-calificaciones (HU-27, HU-45): calificar un pedido completado. ratings guarda una calificacion por pedido y
// por quien califica (ux_order_rater, RN-24); el pedido marca quien ya califico y el perfil suma la reputacion
// (ratingSum, ratingCount, ratingAvg y onTimeCount para "A tiempo").
// Idempotente: se puede correr N veces despues de 019-pedidos-estados.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '020-calificaciones';
const SCHEMA_VERSION = 20;

function ensureCollection(name, validator) {
  const options = { validator: validator, validationLevel: 'strict', validationAction: 'error' };
  if (db.getCollectionNames().includes(name)) {
    db.runCommand(Object.assign({ collMod: name }, options));
  } else {
    db.createCollection(name, options);
  }
}

function setValidator(collection, validator) {
  db.runCommand({ collMod: collection, validator: validator, validationLevel: 'strict', validationAction: 'error' });
}

const id = { bsonType: 'objectId' };
const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };
const term = { bsonType: 'int', minimum: 1, maximum: 20 };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };
const status = { enum: ['pending', 'confirmed', 'completed', 'cancelled'] };
const reason = { enum: ['changed_mind', 'unavailable', 'no_agreement', 'schedule', 'no_show', 'other', 'account_deleted'] };
// "¿Que destacas?" (maqueta Calificar): los de quien vende y los de quien compra.
const tag = { enum: ['on_time', 'as_described', 'good_communication', 'fair_price', 'friendly', 'paid_as_agreed'] };

// ratings (HU-27, HU-45): estrellas de 1 a 5 (RN-25), lo que destaca y un comentario opcional moderado.
ensureCollection('ratings', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'orderId', 'raterId', 'raterName', 'ratedId', 'ratedRole', 'stars', 'tags', 'createdAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      orderId: id,
      raterId: id,
      raterName: name,
      ratedId: id,
      ratedRole: { enum: ['seller', 'buyer'] },
      stars: { bsonType: 'int', minimum: 1, maximum: 5 },
      tags: { bsonType: 'array', maxItems: 5, items: tag },
      comment: { bsonType: 'string', minLength: 1, maxLength: 280 },
      createdAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
db.ratings.createIndex({ orderId: 1, raterId: 1 }, { name: 'ux_order_rater', unique: true });
db.ratings.createIndex({ ratedId: 1, _id: -1 }, { name: 'ix_rated_id' });

// Misma forma que 019-pedidos-estados (collMod reemplaza el validador completo), con ratings.
setValidator('orders', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'number', 'buyerId', 'sellerId', 'buyerName', 'sellerName', 'conversationId',
      'item', 'delivery', 'status', 'history', 'createdAt', 'updatedAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      number: { bsonType: 'int', minimum: 1 },
      buyerId: id,
      sellerId: id,
      buyerName: name,
      sellerName: name,
      conversationId: id,
      item: {
        bsonType: 'object',
        required: ['source', 'sourceId', 'title', 'priceCents', 'quantity', 'totalCents'],
        properties: {
          source: { enum: ['product', 'offer'] },
          sourceId: id,
          productId: id,
          title: { bsonType: 'string', minLength: 1, maxLength: 80 },
          photo: { bsonType: 'string', pattern: '^/media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
          priceCents: { bsonType: ['long', 'int'], minimum: 1, maximum: 99999900 },
          quantity: { bsonType: 'int', minimum: 1, maximum: 10 },
          totalCents: { bsonType: ['long', 'int'], minimum: 1 },
        },
      },
      delivery: {
        bsonType: 'object',
        required: ['pointSlug', 'pointName', 'at'],
        properties: {
          pointSlug: slug,
          pointName: { bsonType: 'string', minLength: 1, maxLength: 60 },
          detail: { bsonType: 'string', minLength: 1, maxLength: 120 },
          at: date,
        },
      },
      payment: {
        bsonType: 'object',
        required: ['method', 'chosenAt'],
        properties: {
          method: { enum: ['cash', 'transfer'] },
          chosenAt: date,
        },
      },
      // RN-19: cada parte confirma su lado; con las dos fechas el pedido se completa.
      handover: {
        bsonType: 'object',
        properties: { buyerAt: date, sellerAt: date },
      },
      // RN-20: quien cancelo (o el sistema, HU-46) y el motivo de la lista.
      // HU-27/HU-45: cuando califico cada parte (una vez por pedido, RN-24).
      ratings: {
        bsonType: 'object',
        properties: { buyerAt: date, sellerAt: date },
      },
      cancellation: {
        bsonType: 'object',
        required: ['by', 'reason', 'at'],
        properties: {
          by: { enum: ['buyer', 'seller', 'system'] },
          reason: reason,
          at: date,
        },
      },
      status: status,
      history: {
        bsonType: 'array', minItems: 1, maxItems: 10,
        items: {
          bsonType: 'object',
          required: ['status', 'at'],
          properties: { status: status, at: date, reason: { bsonType: 'string', maxLength: 40 } },
        },
      },
      createdAt: date,
      updatedAt: date,
      schemaVersion: schemaVersion,
    },
  },
});

// Misma forma que 004-perfiles, con ratingSum y onTimeCount en stats.
setValidator('profiles', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'fullName', 'visibility', 'stats', 'schemaVersion', 'updatedAt'],
    properties: {
      _id: { bsonType: 'objectId' },
      communityId: { bsonType: 'objectId' },
      fullName: { bsonType: 'string', minLength: 2, maxLength: 120 },
      careerSlug: slug,
      term: term,
      avatarKey: { bsonType: 'string', pattern: '^media/avatars/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
      visibility: {
        bsonType: 'object',
        required: ['career', 'sales', 'online'],
        properties: {
          career: { bsonType: 'bool' },
          sales: { bsonType: 'bool' },
          online: { bsonType: 'bool' },
        },
      },
      stats: {
        bsonType: 'object',
        required: ['ratingCount', 'sales', 'purchases'],
        properties: {
          ratingAvg: { bsonType: ['double', 'int'], minimum: 1, maximum: 5 },
          ratingCount: { bsonType: 'int', minimum: 0 },
          // HU-27/HU-45: el promedio es ratingSum / ratingCount; "A tiempo" sale de onTimeCount.
          ratingSum: { bsonType: ['double', 'int'], minimum: 0 },
          onTimeCount: { bsonType: 'int', minimum: 0 },
          sales: { bsonType: 'int', minimum: 0 },
          purchases: { bsonType: 'int', minimum: 0 },
        },
      },
      schemaVersion: schemaVersion,
      updatedAt: date,
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
