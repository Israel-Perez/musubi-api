// 003-dominio-revision (SCRUM-88, spec 02-02): permite el dominio del equipo en el campus UTTT,
// para la cuenta de los revisores de Google Play (revision@musubiapp.com.mx).
// Solo el equipo recibe correo en ese dominio (Cloudflare Email Routing, sin catch-all) y el registro
// exige el codigo enviado a la direccion: nadie de fuera puede crear cuentas con el.
// Idempotente: se puede correr N veces despues de 002-publicaciones.js.

const SCRIPT_ID = '003-dominio-revision';
const SCHEMA_VERSION = 3;
const REVIEW_DOMAIN = 'musubiapp.com.mx';

const result = db.communities.updateOne({ slug: 'uttt' }, { $addToSet: { emailDomains: REVIEW_DOMAIN } });
if (result.matchedCount !== 1) {
  throw new Error(SCRIPT_ID + ': no existe la comunidad uttt (corre antes 001-identidad.js)');
}

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
