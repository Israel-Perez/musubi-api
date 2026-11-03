// 004-perfiles (HU-04): perfil de cada persona, carreras por comunidad y periodo academico del campus.
// Idempotente: se puede correr N veces despues de 003-dominio-revision.js. Catalogo canonico: docs/modelo-datos.md.

const SCRIPT_ID = '004-perfiles';
const SCHEMA_VERSION = 4;

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
const term = { bsonType: 'int', minimum: 1, maximum: 20 };

// communities: el periodo del campus (RNF-21). Opcional: sin el, la API usa semestres (12).
const uttt = db.communities.findOne({ slug: 'uttt' });
if (!uttt) {
  throw new Error(SCRIPT_ID + ': no existe la comunidad uttt (corre antes 001-identidad.js)');
}
db.runCommand({
  collMod: 'communities',
  validator: {
    $jsonSchema: {
      bsonType: 'object',
      required: ['_id', 'slug', 'name', 'shortName', 'emailDomains', 'status', 'schemaVersion', 'createdAt'],
      properties: {
        slug: slug,
        name: { bsonType: 'string', minLength: 2, maxLength: 120 },
        shortName: { bsonType: 'string', minLength: 2, maxLength: 20 },
        emailDomains: {
          bsonType: 'array', minItems: 1, maxItems: 10, uniqueItems: true,
          items: { bsonType: 'string', pattern: '^[a-z0-9.-]+\\.[a-z]{2,}$' },
        },
        status: { enum: ['active', 'inactive'] },
        academicTerm: {
          bsonType: 'object',
          required: ['unit', 'count'],
          properties: {
            unit: { enum: ['cuatrimestre', 'semestre', 'trimestre'] },
            count: term,
          },
        },
        schemaVersion: schemaVersion,
        createdAt: date,
      },
    },
  },
  validationLevel: 'strict',
  validationAction: 'error',
});
// La UTTT cursa 11 cuatrimestres: TSU del 1 al 6, ingenieria y licenciatura del 7 al 11.
db.communities.updateOne({ _id: uttt._id }, { $set: { academicTerm: { unit: 'cuatrimestre', count: NumberInt(11) } } });

// careers: las elige cada persona en "Datos personales" (maqueta Ajustes); las administra HU-41.
ensureCollection('careers', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'slug', 'name', 'shortName', 'level', 'firstTerm', 'lastTerm', 'order', 'active', 'schemaVersion'],
    properties: {
      communityId: { bsonType: 'objectId' },
      slug: slug,
      name: { bsonType: 'string', minLength: 2, maxLength: 120 },
      shortName: { bsonType: 'string', minLength: 2, maxLength: 40 },
      level: { enum: ['tsu', 'engineering', 'bachelor'] },
      firstTerm: term,
      lastTerm: term,
      order: { bsonType: 'int', minimum: 0 },
      active: { bsonType: 'bool' },
      schemaVersion: schemaVersion,
    },
  },
});
db.careers.createIndex({ communityId: 1, slug: 1 }, { name: 'ux_community_slug', unique: true });

// profiles: _id = id del usuario (R13). Se crea con el primer cambio; sin documento valen los
// valores por defecto. Nunca lleva credenciales ni el correo (R10).
ensureCollection('profiles', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'fullName', 'visibility', 'stats', 'schemaVersion', 'updatedAt'],
    properties: {
      _id: { bsonType: 'objectId' },
      communityId: { bsonType: 'objectId' },
      fullName: { bsonType: 'string', minLength: 2, maxLength: 120 },
      careerSlug: slug,
      term: term,
      avatarKey: { bsonType: 'string', pattern: '^media/avatars/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$' },
      visibility: {
        bsonType: 'object',
        required: ['career', 'sales', 'online'],
        properties: {
          career: { bsonType: 'bool' },
          sales: { bsonType: 'bool' },
          online: { bsonType: 'bool' },
        },
      },
      stats: {
        bsonType: 'object',
        required: ['ratingCount', 'sales', 'purchases'],
        properties: {
          ratingAvg: { bsonType: ['double', 'int'], minimum: 1, maximum: 5 },
          ratingCount: { bsonType: 'int', minimum: 0 },
          sales: { bsonType: 'int', minimum: 0 },
          purchases: { bsonType: 'int', minimum: 0 },
        },
      },
      schemaVersion: schemaVersion,
      updatedAt: date,
    },
  },
});
// Perfiles de un campus (HU-41: administracion; HU-05: perfil publico).
db.profiles.createIndex({ communityId: 1 }, { name: 'ix_community' });

// Datos semilla: carreras de la UTTT (propuesta tomada de uttt.edu.mx, a validar con el equipo;
// specs/m02-profile/HU-04-editar-perfil/plan.md §4). Mismo orden y textos que InMemoryCareerRepository.
const tsu = (s, name, shortName) => [s, name, shortName, 'tsu', 1, 6];
const engineering = (s, name, shortName) => [s, name, shortName, 'engineering', 7, 11];
const bachelor = (s, name, shortName) => [s, name, shortName, 'bachelor', 7, 11];
[
  tsu('tsu-energias-renovables-solar', 'TSU en Energías Renovables, área Solar', 'TSU Energías Renovables'),
  tsu('tsu-quimica-industrial', 'TSU en Química, área Industrial', 'TSU Química Industrial'),
  tsu('tsu-quimica-ambiental', 'TSU en Química, área Tecnología Ambiental', 'TSU Química Ambiental'),
  tsu('tsu-nanotecnologia-materiales', 'TSU en Nanotecnología, área Materiales', 'TSU Nanotecnología'),
  tsu('tsu-mantenimiento-industrial', 'TSU en Mantenimiento Industrial, área Industrial', 'TSU Mantenimiento Industrial'),
  tsu('tsu-mantenimiento-maquinaria', 'TSU en Mantenimiento Industrial, área Maquinaria Pesada', 'TSU Maquinaria Pesada'),
  tsu('tsu-mantenimiento-soldadura', 'TSU en Mantenimiento, área Soldadura', 'TSU Soldadura'),
  tsu('tsu-contaduria', 'TSU en Contaduría', 'TSU Contaduría'),
  tsu('tsu-administracion-capital-humano', 'TSU en Administración, área Capital Humano', 'TSU Capital Humano'),
  tsu('tsu-administracion-proyectos', 'TSU en Administración, área Formulación y Evaluación de Proyectos', 'TSU Evaluación de Proyectos'),
  tsu('tsu-procesos-manufactura', 'TSU en Procesos Industriales, área Manufactura', 'TSU Manufactura'),
  tsu('tsu-plantas-hidrocarburos', 'TSU en Construcción y Montaje de Plantas Industriales, área Hidrocarburos', 'TSU Plantas Industriales'),
  tsu('tsu-logistica-transporte', 'TSU en Logística, área Transporte Terrestre', 'TSU Logística Terrestre'),
  tsu('tsu-logistica-suministros', 'TSU en Logística, área Cadena de Suministros', 'TSU Cadena de Suministros'),
  tsu('tsu-negocios-ventas', 'TSU en Desarrollo de Negocios, área Ventas', 'TSU Ventas'),
  tsu('tsu-negocios-mercadotecnia', 'TSU en Desarrollo de Negocios, área Mercadotecnia', 'TSU Mercadotecnia'),
  tsu('tsu-mecatronica-instalaciones', 'TSU en Mecatrónica, área Instalaciones Eléctricas Eficientes', 'TSU Instalaciones Eléctricas'),
  tsu('tsu-mecatronica-robotica', 'TSU en Mecatrónica, área Robótica', 'TSU Robótica'),
  tsu('tsu-mecatronica-automatizacion', 'TSU en Mecatrónica, área Automatización', 'TSU Automatización'),
  tsu('tsu-ti-software', 'TSU en Tecnologías de la Información, área Desarrollo de Software Multiplataforma', 'TSU Desarrollo de Software'),
  tsu('tsu-ti-entornos-virtuales', 'TSU en Tecnologías de la Información, área Entornos Virtuales y Negocios Digitales', 'TSU Entornos Virtuales'),
  tsu('tsu-ti-redes', 'TSU en Tecnologías de la Información, área Infraestructura de Redes Digitales', 'TSU Redes Digitales'),
  engineering('ing-energias-renovables', 'Ingeniería en Energías Renovables', 'Ing. Energías Renovables'),
  engineering('ing-quimica', 'Ingeniería Química', 'Ing. Química'),
  engineering('ing-nanotecnologia', 'Ingeniería en Nanotecnología', 'Ing. Nanotecnología'),
  engineering('ing-ambiental', 'Ingeniería Ambiental', 'Ing. Ambiental'),
  engineering('ing-mantenimiento-industrial', 'Ingeniería en Mantenimiento Industrial', 'Ing. Mantenimiento Industrial'),
  engineering('ing-procesos-industriales', 'Ingeniería en Procesos y Operaciones Industriales', 'Ing. Procesos Industriales'),
  engineering('ing-plantas-industriales', 'Ingeniería en Construcción y Montaje de Plantas Industriales', 'Ing. Plantas Industriales'),
  engineering('ing-mecatronica', 'Ingeniería en Mecatrónica', 'Ing. Mecatrónica'),
  engineering('ing-software', 'Ingeniería en Desarrollo y Gestión de Software', 'Ing. Software'),
  engineering('ing-entornos-virtuales', 'Ingeniería en Entornos Virtuales y Negocios Digitales', 'Ing. Entornos Virtuales'),
  engineering('ing-redes-ciberseguridad', 'Ingeniería en Redes Inteligentes y Ciberseguridad', 'Ing. Redes y Ciberseguridad'),
  bachelor('lic-contaduria', 'Licenciatura en Contaduría', 'Lic. Contaduría'),
  bachelor('lic-capital-humano', 'Licenciatura en Gestión del Capital Humano', 'Lic. Capital Humano'),
  bachelor('lic-negocios-proyectos', 'Licenciatura en Gestión de Negocios y Proyectos', 'Lic. Negocios y Proyectos'),
  bachelor('lic-redes-logisticas', 'Licenciatura en Diseño y Gestión de Redes Logísticas', 'Lic. Redes Logísticas'),
  bachelor('lic-mercadotecnia', 'Licenciatura en Innovación de Negocios y Mercadotecnia', 'Lic. Mercadotecnia'),
].forEach(([careerSlug, name, shortName, level, firstTerm, lastTerm], i) => {
  db.careers.updateOne(
    { communityId: uttt._id, slug: careerSlug },
    {
      $setOnInsert: {
        communityId: uttt._id,
        slug: careerSlug,
        name: name,
        shortName: shortName,
        level: level,
        firstTerm: NumberInt(firstTerm),
        lastTerm: NumberInt(lastTerm),
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
