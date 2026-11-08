// 007-busqueda (HU-09): palabras normalizadas de titulo y descripcion para buscar por prefijo.
// Idempotente: se puede correr N veces despues de 006-chat-bandeja.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '007-busqueda';
const SCHEMA_VERSION = 7;

// Igual que SearchWord.Normalize en la API: minusculas, sin acentos y sin el plural final (s/es).
// La API vuelve a calcular el campo al insertar; esto rellena lo que ya existia.
function words(text) {
  return [
    ...new Set(
      (text || '')
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase()
        .split(/[^\p{L}\p{N}]+/u)
        .filter((word) => word.length > 0)
        .map((word) => {
          if (word.length > 3 && word.endsWith('es')) {
            return word.slice(0, -2);
          }
          return word.length > 2 && word.endsWith('s') ? word.slice(0, -1) : word;
        }),
    ),
  ];
}

// Relleno antes de crear el indice: las publicaciones anteriores no tienen el campo.
let pending = 0;
db.products.find({ searchWords: { $exists: false } }, { title: 1, description: 1 }).forEach((product) => {
  db.products.updateOne(
    { _id: product._id },
    { $set: { searchWords: words(product.title + ' ' + product.description) } },
  );
  pending += 1;
});

// Mismo orden ESR que la vitrina (comunidad y estado primero) y el prefijo como rango sobre las palabras.
db.products.createIndex(
  { communityId: 1, status: 1, searchWords: 1 },
  { name: 'ix_community_status_search' },
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

print(SCRIPT_ID + ': OK (schema version ' + meta.findOne({ _id: 'schema' }).version + ', ' + pending + ' publicaciones rellenadas)');
