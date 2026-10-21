// 001-identidad (spec 02-01): comunidades, usuarios, codigos de verificacion y refresh tokens.
// Idempotente: se puede correr N veces. Usa la base de la cadena de conexion:
//   mongosh "mongodb://usuario:clave@localhost:27017/musubi?authSource=admin" --file db/001-identidad.js
// Catalogo canonico: docs/modelo-datos.md. Cada script sube _meta.version (ver MongoSchema.ExpectedVersion).

const SCRIPT_ID = '001-identidad';
const SCHEMA_VERSION = 1;

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

// communities: un campus por documento; el dominio del correo identifica el campus (RNF-21).
ensureCollection('communities', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'slug', 'name', 'shortName', 'emailDomains', 'status', 'schemaVersion', 'createdAt'],
    properties: {
      slug: { bsonType: 'string', pattern: '^[a-z0-9-]{2,40}$' },
      name: { bsonType: 'string', minLength: 2, maxLength: 120 },
      shortName: { bsonType: 'string', minLength: 2, maxLength: 20 },
      emailDomains: {
        bsonType: 'array', minItems: 1, maxItems: 10, uniqueItems: true,
        items: { bsonType: 'string', pattern: '^[a-z0-9.-]+\\.[a-z]{2,}$' },
      },
      status: { enum: ['active', 'inactive'] },
      schemaVersion: schemaVersion,
      createdAt: date,
    },
  },
});
db.communities.createIndex({ slug: 1 }, { name: 'ux_slug', unique: true });
db.communities.createIndex({ emailDomains: 1 }, { name: 'ux_emailDomains', unique: true });

// users: identidad y credenciales. Nunca se proyecta a una respuesta tal cual (R10).
ensureCollection('users', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'communityId', 'email', 'emailNormalized', 'passwordHash', 'fullName', 'status',
      'consents', 'roles', 'schemaVersion', 'createdAt', 'updatedAt'],
    properties: {
      communityId: { bsonType: 'objectId' },
      email: { bsonType: 'string', minLength: 3, maxLength: 254 },
      emailNormalized: { bsonType: 'string', minLength: 3, maxLength: 254 },
      passwordHash: { bsonType: 'string', minLength: 20, maxLength: 512 },
      fullName: { bsonType: 'string', minLength: 2, maxLength: 120 },
      status: { enum: ['pendingVerification', 'active', 'suspended'] },
      emailVerifiedAt: date,
      consents: {
        bsonType: 'object',
        required: ['termsVersion', 'privacyVersion', 'acceptedAt'],
        properties: {
          termsVersion: { bsonType: 'string', maxLength: 20 },
          privacyVersion: { bsonType: 'string', maxLength: 20 },
          acceptedAt: date,
        },
      },
      roles: { bsonType: 'array', minItems: 1, maxItems: 3, uniqueItems: true, items: { enum: ['student', 'admin'] } },
      schemaVersion: schemaVersion,
      createdAt: date,
      updatedAt: date,
    },
  },
});
db.users.createIndex({ emailNormalized: 1 }, { name: 'ux_emailNormalized', unique: true });
db.users.createIndex({ communityId: 1, status: 1 }, { name: 'ix_community_status' });

// verificationCodes: un codigo vigente por usuario y proposito; se borra solo al vencer (R6).
ensureCollection('verificationCodes', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'purpose', 'codeHash', 'attempts', 'lastSentAt', 'expiresAt', 'schemaVersion', 'createdAt'],
    properties: {
      userId: { bsonType: 'objectId' },
      purpose: { enum: ['emailVerification', 'passwordReset'] },
      codeHash: { bsonType: 'string', minLength: 32, maxLength: 128 },
      attempts: { bsonType: 'int', minimum: 0 },
      lastSentAt: date,
      expiresAt: date,
      schemaVersion: schemaVersion,
      createdAt: date,
    },
  },
});
db.verificationCodes.createIndex({ userId: 1, purpose: 1 }, { name: 'ux_user_purpose', unique: true });
db.verificationCodes.createIndex({ expiresAt: 1 }, { name: 'ttl_expiresAt', expireAfterSeconds: 0 });

// refreshTokens: solo el hash del token; la familia detecta la reutilizacion de un token robado.
ensureCollection('refreshTokens', {
  $jsonSchema: {
    bsonType: 'object',
    required: ['_id', 'userId', 'familyId', 'tokenHash', 'expiresAt', 'schemaVersion', 'createdAt'],
    properties: {
      userId: { bsonType: 'objectId' },
      familyId: { bsonType: 'objectId' },
      tokenHash: { bsonType: 'string', minLength: 32, maxLength: 128 },
      expiresAt: date,
      usedAt: date,
      revokedAt: date,
      device: {
        bsonType: 'object',
        properties: { userAgent: { bsonType: 'string', maxLength: 200 } },
      },
      schemaVersion: schemaVersion,
      createdAt: date,
    },
  },
});
db.refreshTokens.createIndex({ tokenHash: 1 }, { name: 'ux_tokenHash', unique: true });
db.refreshTokens.createIndex({ userId: 1 }, { name: 'ix_user' });
db.refreshTokens.createIndex({ familyId: 1 }, { name: 'ix_family' });
db.refreshTokens.createIndex({ expiresAt: 1 }, { name: 'ttl_expiresAt', expireAfterSeconds: 0 });

// Datos semilla: la comunidad del piloto. Sumar otro campus = otro upsert (sin cambiar codigo).
db.communities.updateOne(
  { slug: 'uttt' },
  {
    $setOnInsert: {
      slug: 'uttt',
      name: 'Universidad Tecnológica de Tula-Tepeji',
      shortName: 'UTTT',
      emailDomains: ['uttt.edu.mx'],
      status: 'active',
      schemaVersion: NumberInt(1),
      createdAt: new Date(),
    },
  },
  { upsert: true },
);

db._meta.updateOne(
  { _id: 'schema' },
  {
    $max: { version: NumberInt(SCHEMA_VERSION) },
    $addToSet: { applied: SCRIPT_ID },
    $set: { updatedAt: new Date() },
  },
  { upsert: true },
);

print(SCRIPT_ID + ': OK (schema version ' + db._meta.findOne({ _id: 'schema' }).version + ')');
