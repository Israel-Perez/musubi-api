// 009-filtros-precio (HU-10): indices para ordenar la vitrina por precio (priceAsc/priceDesc).
// Idempotente: se puede correr N veces despues de 008-reportes-bloqueos.js. Catalogo canonico: docs/modelo-datos.md.
// No modifica documentos: solo crea indices.

const SCRIPT_ID = '009-filtros-precio';
const SCHEMA_VERSION = 9;

// Vitrina por precio ascendente: igualdad (comunidad, estado) y luego la clave de orden, regla ESR (R5).
db.products.createIndex(
  { communityId: 1, status: 1, priceCents: 1, _id: -1 },
  { name: 'ix_community_status_price_id' },
);
// Orden descendente: la direccion de _id tambien importa para que el desempate no se haga en memoria.
db.products.createIndex(
  { communityId: 1, status: 1, priceCents: -1, _id: -1 },
  { name: 'ix_community_status_price_desc_id' },
);

// Igual, con el filtro de una categoria delante.
db.products.createIndex(
  { communityId: 1, categorySlug: 1, status: 1, priceCents: 1, _id: -1 },
  { name: 'ix_community_category_status_price_id' },
);
db.products.createIndex(
  { communityId: 1, categorySlug: 1, status: 1, priceCents: -1, _id: -1 },
  { name: 'ix_community_category_status_price_desc_id' },
);

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
