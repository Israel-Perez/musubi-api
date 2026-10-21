# DatabaseTest

Pruebas contra MongoDB real (`mongo:8.0.32` con Testcontainers.MongoDb), spec `02-01`.

- `MongoContainerFixture`: un contenedor para toda la suite; cada clase pide su base y la fixture le aplica los scripts reales de `db/` con `mongosh`.
- `MongoAuthRepositoryTests`: la batería `TestShared/Auth/AuthRepositoryContract.cs`, la misma que corre `UnitTest` contra los fakes.
- `SchemaScriptTests`: idempotencia del script, índices únicos y TTL, validación `$jsonSchema` y readiness por versión de esquema.
- Requiere Docker. Corre en el job `database` del CI; en local: `dotnet test --project DatabaseTest/DatabaseTest.csproj -c Release`.
