// 014-pedidos (HU-23): pedidos, puntos de entrega del campus y el contador de "MS-NNNN".
// Idempotente: se puede correr N veces despues de 013-favoritos.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '014-pedidos';
const SCHEMA_VERSION = 14;

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
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };
const name = { bsonType: 'string', minLength: 1, maxLength: 120 };
const status = { enum: ['pending', 'confirmed', 'completed', 'cancelled'] };

const uttt = db.communities.findOne({ slug: 'uttt' });
if (!uttt) {
  throw new Error(SCRIPT_ID + ': no existe la comunidad uttt (corre antes 001-identidad.js)');
}

// orders (HU-23): el pedido sale de una conversacion (RN-21) y copia el producto (R8). Nace "pending" (RN-16).
ensureCollection('orders', {
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
// Un pedido abierto por conversacion: un doble toque no duplica (HU-23).
db.orders.createIndex(
  { conversationId: 1 },
  { name: 'ux_conversation_open', unique: true, partialFilterExpression: { status: { $in: ['pending', 'confirmed'] } } },
);
// Los pedidos de una conversacion (el aviso del chat).
db.orders.createIndex({ conversationId: 1, _id: -1 }, { name: 'ix_conversation_id' });
// "Mis pedidos" (HU-25) y "mis ventas" (HU-24), y el borrado de una cuenta (HU-46).
db.orders.createIndex({ buyerId: 1, _id: -1 }, { name: 'ix_buyer_id' });
db.orders.createIndex({ sellerId: 1, _id: -1 }, { name: 'ix_seller_id' });
// RN-07: una publicacion con pedidos abiertos no se elimina.
db.orders.createIndex({ 'item.productId': 1, status: 1 }, { name: 'ix_product_status' });

// meetingPoints (HU-23): los edificios del campus donde se entrega. El de la carrera de quien compra se
// recomienda primero; los administra HU-44.
ensureCollection('meetingPoints', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'slug', 'name', 'careerSlugs', 'order', 'active', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      slug: slug,
      name: { bsonType: 'string', minLength: 2, maxLength: 60 },
      careerSlugs: { bsonType: 'array', maxItems: 40, uniqueItems: true, items: slug },
      order: { bsonType: 'int', minimum: 0 },
      active: { bsonType: 'bool' },
      schemaVersion: schemaVersion,
    },
  },
});
db.meetingPoints.createIndex({ communityId: 1, slug: 1 }, { name: 'ux_community_slug', unique: true });

// counters (HU-23): el consecutivo de los pedidos por campus ("orders:<communityId>"; MS-1001, MS-1002...).
ensureCollection('counters', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'seq'],
    properties: {
      _id: { bsonType: 'string', minLength: 1, maxLength: 60 },
      seq: { bsonType: ['int', 'long'], minimum: 0 },
    },
  },
});

// Datos semilla: los edificios de la UTTT (lista de la usuaria, 10-Nov-2026) y las carreras de cada uno.
[
  ['biblioteca', 'Biblioteca', []],
  ['cafeteria', 'Cafetería', []],
  ['contaduria', 'Contaduría', ['tsu-contaduria', 'tsu-administracion-capital-humano', 'tsu-administracion-proyectos',
    'lic-contaduria', 'lic-capital-humano', 'lic-negocios-proyectos']],
  ['desarrollo-negocios', 'Desarrollo de Negocios', ['tsu-negocios-ventas', 'tsu-negocios-mercadotecnia',
    'tsu-logistica-transporte', 'tsu-logistica-suministros', 'lic-mercadotecnia', 'lic-redes-logisticas']],
  ['idiomas', 'Idiomas', []],
  ['mantenimiento', 'Mantenimiento', ['tsu-mantenimiento-industrial', 'tsu-mantenimiento-maquinaria',
    'tsu-mantenimiento-soldadura', 'ing-mantenimiento-industrial']],
  ['mecatronica', 'Mecatrónica', ['tsu-mecatronica-instalaciones', 'tsu-mecatronica-robotica',
    'tsu-mecatronica-automatizacion', 'ing-mecatronica']],
  ['multimedios', 'Multimedios', []],
  ['procesos-industriales', 'Procesos Industriales', ['tsu-procesos-manufactura', 'tsu-plantas-hidrocarburos',
    'ing-procesos-industriales', 'ing-plantas-industriales']],
  ['quimica', 'Química', ['tsu-energias-renovables-solar', 'tsu-quimica-industrial', 'tsu-quimica-ambiental',
    'tsu-nanotecnologia-materiales', 'ing-energias-renovables', 'ing-quimica', 'ing-nanotecnologia', 'ing-ambiental']],
  ['rectoria', 'Rectoría', []],
  ['tecnologias-informacion', 'Tecnologías de la Información', ['tsu-ti-software', 'tsu-ti-entornos-virtuales',
    'tsu-ti-redes', 'ing-software', 'ing-entornos-virtuales', 'ing-redes-ciberseguridad']],
  ['villas-universitarias', 'Villas Universitarias', []],
  ['vinculacion', 'Vinculación', []],
].forEach(([pointSlug, pointName, careerSlugs], i) => {
  db.meetingPoints.updateOne(
    { communityId: uttt._id, slug: pointSlug },
    {
      $setOnInsert: {
        communityId: uttt._id,
        slug: pointSlug,
        active: true,
        schemaVersion: NumberInt(1),
      },
      $set: { name: pointName, careerSlugs: careerSlugs, order: NumberInt(i + 1) },
    },
    { upsert: true },
  );
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
