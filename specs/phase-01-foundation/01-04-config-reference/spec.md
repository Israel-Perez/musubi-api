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
| `AllowedHosts` | `*` | [01-02 / Implementado] | Se queda en `*`: el ALB solo acepta tráfico de CloudFront y las sondas del balanceador llegan con la IP de la instancia. Restringir con dominio propio (`NOTE (02-05)`) |
| `Proxy:TrustCloudFrontHeaders` | `false` | [02-05 / Implementado] | `true` solo en AWS (lo pone el arranque de la instancia): IP del cliente de `CloudFront-Viewer-Address` y esquema de `CloudFront-Forwarded-Proto` |
| `ASPNETCORE_ENVIRONMENT` (env) | `Production` | [01-02 / Implementado] | `Development` habilita OpenAPI, Scalar y `appsettings.Local.json` |
| `ASPNETCORE_HTTP_PORTS` (env) | `8080` (contenedor) | [01-05 / Implementado] | Puerto del contenedor (`Dockerfile`) |
| `Jwt:Issuer` / `Jwt:Audience` | `musubi-api` / `musubi-app` | [02-03 / Implementado] | `iss` y `aud` de los access tokens |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | `15` / `60` | [02-03 / Implementado] | Vida del access token y del refresh token (desde su última rotación) |
| `Jwt:SigningKey` | — (Development: llave efímera) | [02-03 / Implementado] | **Secreto**: base64 de al menos 32 bytes; user-secrets en local, `Jwt__SigningKey` en SSM. Sin ella la API no arranca fuera de Development |
| `Email:Provider` | `Ses` (base) / `Log` (Development) | [02-03 / Implementado] | `Log` solo en Development (escribe el correo en el log) |
| `Email:From` / `Email:Region` | `""` / `us-east-1` | [02-03 / Implementado] | Remitente verificado en SES; obligatorio fuera de Development |
| `Media:Provider` | `S3` (base) / `InMemory` (Development) | [HU-06 / Implementado] | `InMemory` solo en Development (la API sirve `/media/*`); fuera, S3 detrás de CloudFront |
| `Media:Bucket` / `Media:Region` | `""` / `us-east-1` | [HU-06 / Implementado] | Bucket privado de imágenes (salida `media_bucket` de Terraform; llega como `Media__Bucket` en `user-data`); obligatorio con S3 |
| `Catalog:DefaultCampus` | `uttt` | [HU-06 / Implementado] | Campus de la vitrina para visitantes que no piden otro (HU-48) |
| `RateLimiting:Uploads:PermitLimit` / `RateLimiting:Uploads:WindowSeconds` | `30` / `60` | [HU-06 / Implementado] | Fotos por usuario por ventana (`POST /api/v1/products/photos`) |
| `RateLimiting:Chat:PermitLimit` / `RateLimiting:Chat:WindowSeconds` | `30` / `60` | [HU-20 / Implementado] | Mensajes por usuario por ventana (`POST /api/v1/conversations/{id}/messages`) |
| `RateLimiting:Reports:PermitLimit` / `RateLimiting:Reports:WindowSeconds` | `10` / `3600` | [HU-28 / Implementado] | Reportes por usuario por ventana (`POST /api/v1/reports`; cada uno manda un correo al equipo) |
| `Reports:NotifyEmail` | `""` | [HU-28 / Implementado] | Correo del equipo que revisa los reportes (Parameter Store `/musubi/prod/Reports/NotifyEmail`). Vacío: el reporte solo se guarda y queda en el log. Se lee al arrancar |
| `Moderation:Provider` | `Aws` (base) / `InMemory` (Development) | [02-07 / Implementado] | `InMemory` solo en Development (rechaza el marcador `[[inapropiado]]` y las fotos magenta); fuera, Rekognition para las fotos y el texto según `Moderation:TextProvider` |
| `Moderation:TextProvider` / `Moderation:ExtraBlockedTerms` | `Lexicon` / `[]` | [02-07 / Implementado] | `Lexicon`: léxico local sin costo (el plan gratuito de AWS no incluye Translate ni Comprehend). `Aws`: el léxico y después Translate + Comprehend. Los términos extra (palabras o frases) se suman al léxico sin desplegar |
| `Moderation:Region` / `Moderation:MinImageConfidence` / `Moderation:MaxTextToxicity` / `Moderation:BlockedImageLabels` | `us-east-1` / `80` / `0.7` / 9 etiquetas | [02-07 / Implementado] | Umbrales: confianza de Rekognition (0-100) y toxicidad de Comprehend (0-1) |
| `RateLimiting:Auth:PermitLimit` / `RateLimiting:Auth:WindowSeconds` | `10` / `60` | [02-03 / Implementado] | Intentos por IP en `/auth/*` |
| `Legal:TermsVersion` / `Legal:PrivacyVersion` | `2026-10` / `2026-10` | [HU-47 / Implementado] | Versiones vigentes que se guardan en `users.consents` al registrarse |
| `Cache:ConnectionString` | `""` | [02-02 / Implementado] | Cadena de Redis (secreto: Parameter Store `/musubi/prod/Cache/ConnectionString`). Vacía: caché en memoria de cada instancia. Desde HU-20 también es el backplane de SignalR (canales `musubi-signalr*`): sin ella, el chat solo entrega en tiempo real entre conexiones de la misma instancia. Se lee al arrancar: la instancia toma un valor nuevo al reemplazarse |
| `Firebase:Provider` | `Fcm` (base) / `InMemory` (Development) | [HU-31 / Implementado] | `Fcm`: FCM por la API HTTP v1; `InMemory`: los push se guardan en memoria (Development y pruebas). Sin `Firebase:ServiceAccount`, no sale nada (RNF-15) |
| `Firebase:ServiceAccount` | — (secreto) | [HU-31 / Implementado] | El JSON completo de la llave de la cuenta de servicio de Firebase: user-secrets en local, `/musubi/prod/Firebase/ServiceAccount` (SecureString) en la nube. Nunca en la app |
| `Notifications:ReminderMinutes` | `60` | [HU-31 / Implementado] | Cada cuánto se revisan las solicitudes que vencen mañana o que ya vencieron; `0` lo apaga |
| `GenAi:Provider` / `GenAi:Model` | `Gemini` / `gemini-3.5-flash-lite` (base) · `InMemory` (Development) | [HU-32 / Implementado] | `Gemini`: la API de Gemini (capa gratuita, decisión de la usuaria del 10-Nov-2026); `InMemory`: IA determinista sin red (Development y pruebas); `Off`: sin IA ("Resumir acuerdo" responde `genai.unavailable`). Para probar Gemini en local: `GenAi:Provider=Gemini` en user-secrets o `appsettings.Local.json` |
| `GenAi:TimeoutSeconds` / `GenAi:MaxMessages` / `GenAi:PermitLimit` / `GenAi:WindowMinutes` | `20` / `60` / `6` / `60` | [HU-32 / Implementado] | Espera de la respuesta, mensajes de texto que salen como máximo y resúmenes por persona por ventana (protege la cuota gratuita, OWASP API4) |
| `Gemini:ApiKey` | — (secreto) | [HU-32 / Implementado] | Llave de Google AI Studio: user-secrets en local, `/musubi/prod/Gemini/ApiKey` (SecureString) en la nube. Sin ella no sale nada del servidor (RNF-06) |
| `Payments:*` | — | [HU-55 / Pendiente] | Llave para cifrar la CLABE del vendedor (secreto). `MercadoPago:*` ya no aplica (HU-43 fuera de alcance, D-01) |

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
`AllowedHosts=*` hasta que exista el dominio (02-05). Las claves de HU-31, HU-32 y HU-55 entran a `appsettings.Example.json` solo cuando haya código que las lea.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 21-Oct-2026
- **Detalle:** Tabla inicial con 8 claves implementadas y 4 familias pendientes con su HU dueña. 20-Oct-2026 (SCRUM-66): `ConnectionStrings:Default` (PostgreSQL) → `ConnectionStrings:Mongo` + `Mongo:DatabaseName`; se agrega `Redis:*` como pendiente de 02-02. 21-Oct-2026 (SCRUM-77): `Jwt:*`, `Email:*` y `RateLimiting:Auth:*` implementados (02-03); `AppSettingsTests` impide versionar `SigningKey`. 27-Oct-2026 (SCRUM-23): `Media:*`, `Catalog:DefaultCampus` y `RateLimiting:Uploads:*` (HU-06). 03-Nov-2026 (SCRUM-91): `Redis:*` → `Cache:ConnectionString` (02-02). 03-Nov-2026 (SCRUM-92): `Moderation:*` (02-07). 04-Nov-2026 (SCRUM-29): `RateLimiting:Chat:*` y `Cache:ConnectionString` como backplane de SignalR (HU-20). 08-Nov-2026 (SCRUM-57): `RateLimiting:Reports:*` y `Reports:NotifyEmail` (HU-28).
