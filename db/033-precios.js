// 033-precios (HU-58): el modelo de precios entrena con lo que de verdad se pago en los pedidos completados de cada
// campus. ix_community_status_id da esos pedidos del mas nuevo al mas viejo. Las publicaciones usan el indice que ya
// tenian (products.ix_community_status_id, de 002). Sin cambios de validacion.
// Idempotente: se puede correr N veces despues de 032-recordatorio-entrega.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '033-precios';
const SCHEMA_VERSION = 33;

// "Lo que se pago" por campus: pedidos completados, del mas nuevo al mas viejo.
db.orders.createIndex({ communityId: 1, status: 1, _id: -1 }, { name: 'ix_community_status_id' });

db.getCollection('_meta').updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);
