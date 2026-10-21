# plan.md — 02-01 Modelo de datos MongoDB e identidad

1. **Mapeo de componentes e impacto**
   - **Crear:**
     - `docs/modelo-datos.md`, `db/001-identidad.js` y `db/README.md`;
     - `Platform/Data/{MongoConventions,MongoSchema}.cs` y `Modules/Auth/` (documentos, contratos, `Mongo*`, `InMemory*`, `AddAuthModule`);
     - `TestShared/`, `UnitTest/Data/*` y `DatabaseTest/*`.
   - **Modificar:**
     - `MongoHealthCheck` (lee `_meta`), `Program.cs` (`AddAuthModule`), `nuget.config` y el CI (job `database`);
     - los `.csproj` de UnitTest y DatabaseTest (enlazan `TestShared`).
2. **Decisiones de arquitectura**
   - **El script `mongosh` es la fuente del esquema**, no la API al arrancar: dos instancias no compiten por crear índices, y el script es exactamente el entregable de la evaluación. La API solo verifica la versión (readiness).
   - **Atomicidad de un documento en lugar de transacciones:** funciona igual en Atlas M0 y con N instancias.
   - **`users` separado de `profiles`:** las credenciales nunca viajan con los datos públicos.
   - **Una batería de contrato compartida** en lugar de pruebas separadas para el fake y para Mongo: evita la deriva entre ambos (skill `deferred-scope-fakes`).
   - **Estados como `string` constantes:** el validador `$jsonSchema` y el código usan exactamente el mismo texto.
   - **Testcontainers.MongoDb 4.16.0** (MIT, publicado el 08-Oct-2026, supera el `cooldown`). Dependencias transitivas agregadas al Source Mapping: `Testcontainers*`, `Docker.DotNet.*`, `SSH.NET`, `SharpZipLib` y `BouncyCastle.Cryptography`. Alternativa descartada: Mongo2Go, que descarga binarios sin versión fija y está sin mantenimiento.
3. **Guardarraíles DevSecOps**
   - Validación en el servidor (R13): ni un script manual puede guardar un usuario incompleto.
   - Solo hashes de códigos y tokens.
   - Índices únicos en lugar de chequeos de aplicación, para manejar carreras entre instancias.
   - `SchemaVersionTests` como drift guard entre `db/` y el código.
4. **Validación:** UnitTest (fakes + convenciones + drift); DatabaseTest en el CI (Mongo real); self-test de mutación del fake.
5. **Secuencia:** catálogo → script → convenciones → contratos → `Mongo*` → `InMemory*` → batería compartida → DatabaseTest → job del CI.
