# 01-04 — Referencia de configuración

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:01-04`

## Contexto
Tabla canónica de las claves de configuración de musubi-api y dónde vive cada secreto. **Depende de**: `01-01`.

## Requisitos
1. Tabla canónica de claves con default, estado/fase y descripción.
2. Cada clave de `appsettings.Example.json` corresponde a código real; las claves futuras solo viven en esta tabla.
3. Ningún archivo versionado contiene secretos.
4. Los secretos locales viven en user-secrets o en `appsettings.Local.json` (ignorado, cargado solo en Development).

## Diseño

| Clave / variable | Default | Estado / Fase | Descripción |
|---|---|---|---|
| `UseInMemoryDatabase` | `false` (base) / `true` (Development) | [01-03 / Implementado] | `true` = sin MongoDB (local y pruebas; readiness Healthy sin consulta); `false` = MongoDB |
| `ConnectionStrings:Mongo` | `""` | [01-03 / Implementado] | URI `mongodb://` (local) o `mongodb+srv://` (Atlas). **Secreto** si lleva usuario y clave: user-secrets en local, secreto del despliegue en AWS |
| `Mongo:DatabaseName` | `musubi` | [01-03 / Implementado] | Nombre de la base |
| `Kestrel:Limits:MaxRequestBodySize` | `10485760` | [01-02 / Implementado] | Cuerpo máximo (10 MB) |
| `Kestrel:Limits:KeepAliveTimeout` | `00:02:00` | [01-02 / Implementado] | Keep-alive defensivo |
| `Cors:AllowedOrigins` | `[]` | [01-02 / Implementado] | Orígenes permitidos (la app Android no usa CORS; aplica a un panel web futuro) |
| `AllowedHosts` | `*` | [01-02 / Implementado] | Restringir al dominio real en `02-05` |
| `ASPNETCORE_ENVIRONMENT` (env) | `Production` | [01-02 / Implementado] | `Development` habilita OpenAPI, Scalar y `appsettings.Local.json` |
| `ASPNETCORE_HTTP_PORTS` (env) | `8080` (contenedor) | [01-05 / Implementado] | Puerto del contenedor (`Dockerfile`) |
| `Redis:*` | — | [02-02 / Pendiente] | Cadena de Redis/Valkey (secreto si lleva clave) |
| `Jwt:*` | — | [HU-02 / Pendiente] | Emisor, audiencia y llave (secreto) |
| `Firebase:*` | — | [HU-31 / Pendiente] | Credenciales de Firebase Admin (secreto, solo back) |
| `GenAi:*` | — | [HU-32 / Pendiente] | Proveedor, modelo y llave del LLM (secreto) |
| `MercadoPago:*` | — | [HU-43 / Pendiente] | Access token de pruebas (secreto) y webhook |

Archivos:

- `appsettings.json`: defaults seguros versionados (sin secretos).
- `appsettings.Development.json`: `UseInMemoryDatabase=true` para que `dotnet run` funcione sin MongoDB.
- `appsettings.Production.json`: `UseInMemoryDatabase=false`, CORS vacío; valores reales por variables/secretos del despliegue.
- `appsettings.Example.json`: documenta las claves implementadas con placeholders.
- Locales (ignorados): `appsettings.Local.json`, `appsettings.*.Local.json`, `*.secrets.json`, `deploy/.env`.
- User-secrets: `UserSecretsId=musubi-api-dev` (`dotnet user-secrets --project Musubi.Api set ...`).

## Contratos
Claves de la tabla. Las variables de entorno siguen la convención de .NET (`ConnectionStrings__Mongo`, `Mongo__DatabaseName`, `Cors__AllowedOrigins__0`).

## Tests
- `UnitTest/Common/AppSettingsTests.cs` (11): los 4 appsettings versionados son JSON válido y no contienen secretos (password en connection string, URI `mongodb://usuario:clave@`, propiedades `password/secret/apikey/token` con valor, llaves PEM, cadenas de 48 o más caracteres base64/hex); Example documenta las claves implementadas y no incluye claves futuras; Production desactiva InMemory.

## Criterios
- `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` → 23/23.
- `pwsh scripts/critic-guardrails.ps1` → PASS (B2b: connection string con password o URI `mongodb://` con credenciales en appsettings versionados; self-test con un appsettings de prueba → FAIL, 20-Oct-2026).
- `semgrep scan --config .semgrep/semgrep.yaml` → 0 hallazgos de `musubi-connection-string-with-password`.

## Límites
`AllowedHosts=*` hasta que exista el dominio (02-05). Las claves de HU-02, HU-31, HU-32 y HU-43 entran a `appsettings.Example.json` solo cuando haya código que las lea.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 20-Oct-2026
- **Detalle:** Tabla inicial con 8 claves implementadas y 4 familias pendientes con su HU dueña. 20-Oct-2026 (SCRUM-66): `ConnectionStrings:Default` (PostgreSQL) → `ConnectionStrings:Mongo` + `Mongo:DatabaseName`; se agrega `Redis:*` como pendiente de 02-02.
