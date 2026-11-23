// 032-recordatorio-entrega (HU-62): el recordatorio una hora antes de la entrega y "Ya llegue".
// orders.handover suma buyerArrivedAt y sellerArrivedAt (cuando cada parte aviso que ya esta en el punto).
// ix_confirmed_delivery da las entregas de la proxima hora de los pedidos confirmados (parcial: solo esos). El centro
// de avisos acepta delivery_reminder y order_arrived.
// Idempotente: se puede correr N veces despues de 031-categorias.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '032-recordatorio-entrega';
const SCHEMA_VERSION = 32;

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
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };
const status = { enum: ['pending', 'confirmed', 'completed', 'cancelled'] };
const reason = { enum: ['changed_mind', 'unavailable', 'no_agreement', 'schedule', 'no_show', 'other', 'account_deleted'] };

// Igual que en 027-pagos (collMod reemplaza el validador completo), con "Ya llegue".
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
      // HU-55: la transferencia (referencia, aviso, "todavia no me llega" y validacion) y el codigo de entrega del efectivo.
      payment: {
        bsonType: 'object',
        required: ['method', 'chosenAt'],
        properties: {
          method: { enum: ['cash', 'transfer'] },
          chosenAt: date,
          reference: { bsonType: 'string', pattern: '^[A-Z0-9-]{4,30}$' },
          reportedAt: date,
          notReceivedAt: date,
          validatedAt: date,
          deliveryCode: { bsonType: 'string', pattern: '^[0-9]{4}$' },
          codeAttempts: { bsonType: 'int', minimum: 0, maximum: 5 },
        },
      },
      // RN-19: cada parte confirma su lado; con las dos fechas el pedido se completa. HU-62: "Ya llegue" de cada parte.
      handover: {
        bsonType: 'object',
        properties: { buyerAt: date, sellerAt: date, buyerArrivedAt: date, sellerArrivedAt: date },
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

// "Tu entrega del pedido #MS-0012": los confirmados con entrega en la proxima hora, del mas proximo al mas lejano.
db.orders.createIndex(
  { 'delivery.at': 1 },
  { name: 'ix_confirmed_delivery', partialFilterExpression: { status: 'confirmed' } },
);

// notifications (HU-31): igual que en 030, con los avisos de la entrega.
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
          'report_reviewed', 'product_removed',
          // HU-62
          'delivery_reminder', 'order_arrived'],
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
