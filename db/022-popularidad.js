// 022-popularidad (HU-49): métricas agregadas por producto/día UTC, sin identidad.
const SCRIPT_ID = '022-popularidad';
const SCHEMA_VERSION = 22;
const options = {
  validator: { $jsonSchema: { bsonType: 'object', required: ['_id', 'communityId', 'productId', 'day', 'views', 'expiresAt'], properties: {
    _id: { bsonType: 'string' }, communityId: { bsonType: 'objectId' }, productId: { bsonType: 'objectId' },
    day: { bsonType: 'date' }, views: { bsonType: ['int', 'long'], minimum: 1 }, expiresAt: { bsonType: 'date' },
  } } }, validationLevel: 'strict', validationAction: 'error',
};
if (db.getCollectionNames().includes('productStatsDaily')) db.runCommand(Object.assign({ collMod: 'productStatsDaily' }, options));
else db.createCollection('productStatsDaily', options);
const stats = db.getCollection('productStatsDaily');
stats.createIndex({ communityId: 1, productId: 1, day: 1 }, { unique: true, name: 'ux_product_day' });
stats.createIndex({ communityId: 1, day: 1, views: -1 }, { name: 'ix_community_day_views' });
stats.createIndex({ expiresAt: 1 }, { expireAfterSeconds: 0, name: 'ttl_expiresAt' });
db.getCollection('_meta').updateOne({ _id: 'schema' }, { $max: { version: NumberInt(SCHEMA_VERSION) }, $addToSet: { applied: SCRIPT_ID }, $set: { updatedAt: new Date() } }, { upsert: true });
