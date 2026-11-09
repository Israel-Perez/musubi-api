// 011-ofertas (HU-16): ofertas "Yo tengo este producto" a las solicitudes de compra.
// Idempotente: se puede correr N veces despues de 010-filtros-precio.js.
// Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '011-ofertas';
const SCHEMA_VERSION = 11;

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

// offers (HU-16): una publicacion propia vinculada ("listing") o un producto sin publicar ("direct", RN-13).
// Una por persona y solicitud (RN-14). HU-17 agrega accepted y rejected.
ensureCollection('offers', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'requestId', 'requesterId', 'sellerId', 'sellerName', 'kind', 'title', 'priceCents', 'condition', 'status', 'createdAt', 'updatedAt', 'schemaVersion'],
    properties: {
      _id: id,
      communityId: id,
      requestId: id,
      requesterId: id,
      sellerId: id,
      // Copia del nombre corto ("Ana R.", R8).
      sellerName: { bsonType: 'string', minLength: 1, maxLength: 60 },
      kind: { enum: ['listing', 'direct'] },
      productId: id,
      // Nombre de la publicacion (copia) o descripcion de la oferta directa.
      title: { bsonType: 'string', minLength: 3, maxLength: 300 },
      priceCents: { bsonType: 'long', minimum: 100, maximum: 99999900 },
      condition: { enum: ['new', 'used'] },
      photoKey: { bsonType: 'string', minLength: 1, maxLength: 200 },
      message: { bsonType: 'string', minLength: 1, maxLength: 300 },
      status: { enum: ['sent'] },
      createdAt: date,
      updatedAt: date,
      schemaVersion: schemaVersion,
    },
  },
});
// RN-14: una oferta por persona y solicitud (tambien resuelve dos envios a la vez).
db.offers.createIndex({ requestId: 1, sellerId: 1 }, { name: 'ux_request_seller', unique: true });
// Ofertas de una solicitud, de la mas nueva a la mas vieja (HU-17).
db.offers.createIndex({ requestId: 1, _id: -1 }, { name: 'ix_request_id' });
// Borrado de cuenta (HU-46): las que hizo y las que recibio.
db.offers.createIndex({ sellerId: 1 }, { name: 'ix_seller' });
db.offers.createIndex({ requesterId: 1 }, { name: 'ix_requester' });

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
