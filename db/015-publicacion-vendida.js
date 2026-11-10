// 015-publicacion-vendida (HU-08): "Marcar como vendido" (maqueta Mis-productos). Una publicacion puede quedar
// vendida (status "sold") con la fecha de la venta (soldAt).
// Idempotente: se puede correr N veces despues de 014-pedidos.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '015-publicacion-vendida';
const SCHEMA_VERSION = 15;

const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };

// Misma forma que 002-publicaciones (collMod reemplaza el validador completo): status admite "sold" y se agrega
// soldAt. searchWords (007) no necesita regla: el validador no prohibe campos extra.
db.runCommand({
  collMod: 'products',
  validator: {
    $jsonSchema: {
      bsonType: 'object',
      required: ['_id', 'communityId', 'sellerId', 'seller', 'title', 'description', 'categorySlug', 'priceCents',
        'condition', 'stock', 'status', 'photos', 'schemaVersion', 'createdAt', 'updatedAt'],
      properties: {
        communityId: { bsonType: 'objectId' },
        sellerId: { bsonType: 'objectId' },
        seller: {
          bsonType: 'object',
          required: ['name'],
          properties: { name: { bsonType: 'string', maxLength: 80 } },
        },
        title: { bsonType: 'string', minLength: 3, maxLength: 80 },
        description: { bsonType: 'string', maxLength: 500 },
        categorySlug: slug,
        priceCents: { bsonType: ['long', 'int'], minimum: 1, maximum: 99999900 },
        condition: { enum: ['new', 'used'] },
        conditionDetail: { enum: ['likeNew', 'good', 'fair'] },
        stock: { bsonType: 'int', minimum: 0 },
        status: { enum: ['active', 'soldOut', 'hidden', 'removed', 'sold'] },
        soldAt: date,
        photos: {
          bsonType: 'array', minItems: 1, maxItems: 8, uniqueItems: true,
          items: { bsonType: 'string', pattern: '^media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
        },
        schemaVersion: schemaVersion,
        createdAt: date,
        updatedAt: date,
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
