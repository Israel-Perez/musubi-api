// 002-publicaciones (HU-06, HU-50): categorias por comunidad y publicaciones de productos.
// Idempotente: se puede correr N veces despues de 001-identidad.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '002-publicaciones';
const SCHEMA_VERSION = 2;

function ensureCollection(name, validator) {
  const options = { validator: validator, validationLevel: 'strict', validationAction: 'error' };
  if (db.getCollectionNames().includes(name)) {
    db.runCommand(Object.assign({ collMod: name }, options));
  } else {
    db.createCollection(name, options);
  }
}

const date = { bsonType: 'date' };
const schemaVersion = { bsonType: 'int', minimum: 1 };
const slug = { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' };

// categories: las elige quien publica y filtra (maqueta Vender-3); las administra HU-41.
ensureCollection('categories', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'slug', 'name', 'order', 'active', 'schemaVersion'],
    properties: {
      communityId: { bsonType: 'objectId' },
      slug: slug,
      name: { bsonType: 'string', minLength: 2, maxLength: 40 },
      order: { bsonType: 'int', minimum: 0 },
      active: { bsonType: 'bool' },
      schemaVersion: schemaVersion,
    },
  },
});
db.categories.createIndex({ communityId: 1, slug: 1 }, { name: 'ux_community_slug', unique: true });

// products: la publicacion (HU-06). Fotos en S3 (R12), copia del vendedor (R8), stock atomico (R7).
ensureCollection('products', {
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
      status: { enum: ['active', 'soldOut', 'hidden', 'removed'] },
      photos: {
        bsonType: 'array', minItems: 1, maxItems: 8, uniqueItems: true,
        items: { bsonType: 'string', pattern: '^media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
      },
      schemaVersion: schemaVersion,
      createdAt: date,
      updatedAt: date,
    },
  },
});
// Vitrina: igualdad (comunidad, estado) y orden por _id (cursor), regla ESR (R5).
db.products.createIndex({ communityId: 1, status: 1, _id: -1 }, { name: 'ix_community_status_id' });
// "Filtrar" por categoria y rango de precios de la categoria (Vender-3).
db.products.createIndex({ communityId: 1, categorySlug: 1, status: 1, _id: -1 }, { name: 'ix_community_category_status_id' });
// "Mis productos".
db.products.createIndex({ sellerId: 1, _id: -1 }, { name: 'ix_seller_id' });

// Datos semilla: las categorias de la maqueta para la comunidad del piloto.
const uttt = db.communities.findOne({ slug: 'uttt' });
if (!uttt) {
  throw new Error('Falta la comunidad uttt: corre antes db/001-identidad.js');
}
[
  ['calculadoras', 'Calculadoras'],
  ['libros', 'Libros'],
  ['laptops', 'Laptops'],
  ['laboratorio', 'Laboratorio'],
  ['dibujo', 'Dibujo'],
  ['electronica', 'Electrónica'],
].forEach(([categorySlug, name], i) => {
  db.categories.updateOne(
    { communityId: uttt._id, slug: categorySlug },
    {
      $setOnInsert: {
        communityId: uttt._id,
        slug: categorySlug,
        name: name,
        order: NumberInt(i + 1),
        active: true,
        schemaVersion: NumberInt(1),
      },
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
