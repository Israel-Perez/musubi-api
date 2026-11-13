// 021-ventas-compras (HU-26): "Ventas" y "Compras" del Perfil cuentan los pedidos completados. Desde esta version la
// API los suma al completarse cada pedido; este script cuenta los que ya existian y deja el numero exacto.
// Idempotente: $set con la cuenta real, asi que se puede correr N veces despues de 020-calificaciones.js (infra-up
// corre todos los scripts en cada despliegue y el resultado no cambia). Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '021-ventas-compras';
const SCHEMA_VERSION = 21;

// Cuantos pedidos completados tiene cada persona en un lado del pedido, con su campus y su nombre (copias del pedido).
function completedBy(field, nameField) {
  return db.orders.aggregate([
    { $match: { status: 'completed' } },
    { $group: { _id: '$' + field, count: { $sum: 1 }, communityId: { $first: '$communityId' }, fullName: { $first: '$' + nameField } } },
  ]).toArray();
}

// Solo cuentas que siguen existiendo: un perfil borrado (HU-46) no se vuelve a crear.
function apply(groups, counter, other) {
  let updated = 0;
  groups.forEach((g) => {
    if (!db.users.findOne({ _id: g._id }, { _id: 1 })) {
      return;
    }

    const set = {};
    set['stats.' + counter] = NumberInt(g.count);
    const onInsert = {
      communityId: g.communityId,
      fullName: g.fullName,
      visibility: { career: true, sales: true, online: false },
      schemaVersion: NumberInt(1),
    };
    onInsert['stats.' + other] = NumberInt(0);
    onInsert['stats.ratingCount'] = NumberInt(0);
    db.profiles.updateOne({ _id: g._id }, { $set: Object.assign(set, { updatedAt: new Date() }), $setOnInsert: onInsert }, { upsert: true });
    updated += 1;
  });
  return updated;
}

const sellers = apply(completedBy('sellerId', 'sellerName'), 'sales', 'purchases');
const buyers = apply(completedBy('buyerId', 'buyerName'), 'purchases', 'sales');

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

print(SCRIPT_ID + ': OK (' + sellers + ' con ventas, ' + buyers + ' con compras; schema version ' + meta.findOne({ _id: 'schema' }).version + ')');
