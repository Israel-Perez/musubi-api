# 02-01 — Modelo de datos MongoDB e identidad

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-76 / S2 / M-01 (colecciones de identidad) / contraparte: N/A (la app solo ve el contrato HTTP)

## Contexto
El lineamiento 14 exige una base NoSQL, y el login (HU-01, HU-02, HU-03 y HU-47) necesita colecciones de identidad. Esta spec fija las reglas para que el modelo de las 55 HU escale sin rediseño e implementa la parte de identidad. **Depende de**: `01-03` (cliente de MongoDB y readiness) y `01-04` (configuración).

## Requisitos
1. Publicar el modelo de documentos de las 55 HU con reglas de escalabilidad verificables en el catálogo canónico [`docs/modelo-datos.md`](../../../docs/modelo-datos.md):
   - multi-comunidad (RNF-21);
   - ids ordenados por tiempo;
   - sin arreglos sin límite;
   - versión por documento;
   - índices ESR;
   - TTL;
   - atomicidad de un solo documento;
   - datos sensibles separados.
2. Crear con un script `mongosh` idempotente (`db/001-identidad.js`) las colecciones `communities`, `users`, `verificationCodes`, `refreshTokens` y `_meta`, con validación `$jsonSchema` estricta, índices con nombre y la UTTT como semilla. Es el script que se entrega (lineamiento 19).
3. Exponer repositorios de identidad con dos implementaciones de comportamiento idéntico: MongoDB y en memoria (`UseInMemoryDatabase=true`).
4. Hacer que `/health/ready` responda Unhealthy si la base no está al día con `MongoSchema.ExpectedVersion`, para que el balanceador no mande tráfico a una instancia con esquema viejo.
5. Garantizar las reglas críticas en la base:
   - correo único con concurrencia;
   - un código vigente por usuario y propósito;
   - consumo atómico de un refresh token (una sola rotación aunque lleguen dos peticiones a la vez).

## Diseño
- **Reglas R1–R14 y colecciones:** ver [`docs/modelo-datos.md`](../../../docs/modelo-datos.md) (no se copian aquí).
- **Convenciones BSON** en `Platform/Data/MongoConventions.cs`: camelCase, omitir nulos e ignorar campos extra, registradas una vez por proceso.
- **Fechas e ids:** fechas `DateTime` UTC; ids `string` con representación ObjectId.
- **Estados como texto en minúsculas** (`pendingVerification`), no como enums de C#, para que coincidan exactamente con el `enum` del validador.
- **Ubicación del módulo:** `Modules/Auth/Data/` contiene los documentos (`AuthDocuments.cs`), los contratos (`AuthRepositories.cs`) y las implementaciones `Mongo*` e `InMemory*`. Se registra con `AddAuthModule()`, que elige la implementación de forma lazy según la bandera (00-04, regla 3). El módulo se crea antes que su primera HU porque HU-01 depende de él.
- **Paridad del fake:** la batería `TestShared/Auth/AuthRepositoryContract.cs` se enlaza en `UnitTest` (fakes) y en `DatabaseTest` (Mongo real con `db/` aplicado). Si el fake se aleja de la base, falla.
- **Readiness:** `MongoHealthCheck` lee `_meta.schema` y con esa lectura comprueba a la vez la conexión y la versión.

## Contratos
- Esquema: [`docs/modelo-datos.md`](../../../docs/modelo-datos.md), filas `_meta`, `communities`, `users`, `verificationCodes` y `refreshTokens`, más `db/001-identidad.js`.
- Sin endpoints nuevos. La configuración no cambia (`ConnectionStrings:Mongo`, `Mongo:DatabaseName`; ver 01-04).

## Tests
- `UnitTest/Data/InMemoryAuthRepositoryTests.cs` (13): la batería de contrato sobre los fakes.
- `UnitTest/Data/MongoConventionsTests.cs` (3): forma BSON (camelCase, ObjectId, `int32`, sin nulos) y tolerancia a campos de versiones futuras.
- `UnitTest/Data/SchemaVersionTests.cs` (2): scripts numerados de forma consecutiva; el último declara `ExpectedVersion` y su `SCRIPT_ID`.
- `UnitTest/DI/ServiceRegistrationTests.cs` (+2): la bandera elige los fakes o los repositorios de Mongo.
- `DatabaseTest/MongoAuthRepositoryTests.cs` (13): la misma batería contra `mongo:8.0.32` (Testcontainers) con `db/` aplicado.
- `DatabaseTest/SchemaScriptTests.cs` (5):
  - el script es idempotente;
  - existen los índices únicos y TTL;
  - el validador rechaza documentos incompletos o con estados desconocidos;
  - la readiness da Unhealthy sin scripts y Healthy con ellos.

## Criterios
- `dotnet build Musubi.slnx -c Release` → 0/0.
- `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → 43/43.
- Job `database` del CI (requiere Docker) → 18/18.
- Self-test: si el fake permite consumir dos veces un refresh token, 2 pruebas de contrato fallan (21-Oct-2026).
- Restore con caché vacía sin NU1100 (Source Mapping con los transitivos de Testcontainers).

## Límites
- Solo se implementan las colecciones de identidad. Las demás quedan en 📐 Diseño y nacen con su HU, cada una con su script `NNN-*.js`.
- `DatabaseTest` no corre en la máquina de creación (no hay Docker); se verifica en el job `database` del CI.
- El monitor TTL de MongoDB borra con un retraso de unos 60 s, por eso los repositorios también filtran por `expiresAt`.
- Recuperar la contraseña (pantalla de la maqueta) usa `verificationCodes` con el propósito `passwordReset`, pero no tiene HU todavía (`NOTE (HU-56)`, por crear).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Modelo escalable (R1–R14) para las 55 HU; implementa la identidad. Paquete nuevo de pruebas: Testcontainers.MongoDb 4.16.0 (MIT), ya previsto en la constitución §6.
