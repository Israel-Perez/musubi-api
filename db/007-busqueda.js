// 007-busqueda (HU-09): indice de texto en espanol para buscar productos por palabra clave.
// Idempotente: se puede correr N veces despues de 006-chat-bandeja.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '007-busqueda';
const SCHEMA_VERSION = 7;

// MongoDB permite un solo indice de texto por coleccion: cubre titulo y descripcion juntos.
// default_language en espanol da los acentos, los plurales y las palabras vacias del idioma, sin
// duplicar un campo normalizado (R12). Los pesos quedan listos por si se ordena por relevancia.
db.products.createIndex(
  { title: 'text', description: 'text' },
  {
    name: 'tx_title_description',
    default_language: 'spanish',
    weights: { title: 3, description: 1 },
  },
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
