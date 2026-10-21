# 00-03 — Requerimientos no funcionales (musubi-api)

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:00-03`

## Contexto
Umbrales medibles de los RNF del MVP que aplican al back, más los NFR técnicos de calidad. La columna **Aplica** indica si el RNF también está en `musubi-app:00-03`, donde cada repo verifica su lado. **Depende de**: `00-01`.

## Requisitos
1. Cada NFR tiene métrica o umbral, estado, herramienta y comando de verificación.
2. Los umbrales no medidos se declaran como "🎯 Objetivo (Por medir)" o "🎯 Objetivo (Por definir con el PO)", con su spec dueña.
3. Los umbrales se fijan midiendo (principio 6); ningún número se inventa.

## Diseño

### RNF del MVP que aplican al back

| NFR | Categoría | Aplica | Métrica / Umbral | Estado del umbral | Herramienta | Comando / verificación |
|---|---|---|---|---|---|---|
| RNF-01 Credenciales cifradas | Seguridad | Ambos | Contraseñas con hash adaptativo (nunca reversible); 0 credenciales en claro en la BD, los logs o las respuestas | 📈 Medido real (hash PBKDF2-SHA512, 210 000 iteraciones, 21-Oct-2026) / 🎯 respuestas — HU-01/HU-02 | UnitTest + SecurityTest | `UnitTest/Auth/PasswordTests.cs`; pruebas de HU-01 |
| RNF-02 Comunicación cifrada | Seguridad | Ambos | HTTPS obligatorio; HSTS de 365 días fuera de Dev | 📈 Medido real (configuración) | HSTS + HttpsRedirection | `SecurityTest/Hsts/HstsTests.cs` (3 pruebas) |
| RNF-03 Solo el dueño modifica; solo el admin modera | Seguridad | Back | 403 para quien no es dueño en cada endpoint de escritura | 🎯 Objetivo (Por medir) — HU-07, HU-14, HU-39 | SecurityTest | Pruebas de autorización por HU |
| RNF-04 Moderación registrada | Seguridad | Back | 100 % de las acciones de moderación con registro (quién, qué, cuándo) | 🎯 Objetivo (Por medir) — HU-39/HU-40 | IntegrationTest | Pruebas de HU-39 |
| RNF-05 Datos mínimos y aviso de privacidad | Privacidad | Ambos | El registro exige aceptar el aviso vigente; los DTOs de respuesta no exponen datos innecesarios | 🎯 Objetivo (Por medir) — HU-47 | ContractTest + critic B2 | Fixtures de contrato de HU-01/HU-47 |
| RNF-06 IA solo a petición y con datos mínimos | Privacidad | Ambos | Ninguna conversación sale a la IA sin la petición explícita de un participante; solo con los campos mínimos | 🎯 Objetivo (Por medir) — HU-32 | UnitTest (fake del proveedor) | Pruebas de HU-32 |
| RNF-07 Búsquedas e historial privados | Privacidad | Back | Ningún endpoint expone el historial de otro usuario; la analítica es solo agregada | 🎯 Objetivo (Por medir) — HU-09/HU-12 | SecurityTest | Pruebas de HU-12 |
| RNF-08 Eliminación de cuenta y datos | Privacidad | Ambos | Eliminación (o anonimización documentada) de los datos personales | 🎯 Objetivo (Por medir) — HU-46 | IntegrationTest + DatabaseTest | Pruebas de HU-46 |
| RNF-09 Búsquedas en tiempo aceptable | Rendimiento | Ambos | p95 ≤ N s (N por definir) | 🎯 Objetivo (Por definir con el PO) | Prueba de carga (pendiente) | `NOTE (02-06)` |
| RNF-10 Chat casi en tiempo real | Rendimiento | Ambos | Entrega de un mensaje ≤ N s (N por definir) | 🎯 Objetivo (Por definir con el PO) | SignalR + prueba de integración | `NOTE (HU-20)` |
| RNF-11 Coincidencias en segundo plano | Rendimiento | Back | La petición de publicar no espera el cálculo de coincidencias | 🎯 Objetivo (Por medir) — HU-18/HU-19 | IntegrationTest | Pruebas de HU-18 |
| RNF-13 Fotos optimizadas | Rendimiento | Ambos | El back rechaza cuerpos de más de 10 MB (Kestrel); el tamaño final de la foto está por definir | 📈 Medido real (límite) / 🎯 tamaño por definir | Límites de Kestrel | `UnitTest/DI/ServiceRegistrationTests.cs` (`Kestrel_LimitsAreBoundFromConfiguration`) |
| RNF-14 Disponibilidad en el piloto | Disponibilidad | Back | ≥ N % (por definir) | 🎯 Objetivo (Por definir con el PO) | `/health` y `/health/ready` + monitoreo | `NOTE (02-05)` |
| RNF-15 Degradación de IA/ML | Disponibilidad | Ambos | Con la IA o el ML caídos, búsqueda, chat y pedidos responden 2xx | 🎯 Objetivo (Por medir) — HU-32, HU-35 | IntegrationTest con un fake que falla | Pruebas de HU-32/HU-35 |
| RNF-16 Respaldos de la BD | Disponibilidad | Back | Respaldo exportable con `mongodump` y script de recreación con `mongosh` (Atlas M0 no ofrece respaldos automáticos) | 🎯 Objetivo (Por definir con el PO) | `mongodump` + script `mongosh` | `NOTE (02-05)` |
| RNF-18 Errores claros en español | Usabilidad | Ambos | Errores de validación con un mensaje entendible y sin detalle interno; `code` estable para traducir | 📈 Medido real (21-Oct-2026) | ProblemDetails + `docs/errores.md` | `SecurityTest/Errors/*`, `ContractTest/ProblemContractTests.cs` |
| RNF-21 Más universidades sin rediseño | Escalabilidad | Ambos | La comunidad es un dato del modelo | 🎯 Objetivo (Por medir) — 02-01 | Revisión del modelo + DatabaseTest | `NOTE (02-01)` |
| RNF-22 Carga del piloto | Escalabilidad | Back | N usuarios concurrentes (por definir) | 🎯 Objetivo (Por definir con el PO) | Prueba de carga (pendiente) | `NOTE (02-06)` |
| RNF-23 Organización por módulos; IA/ML desacoplados | Mantenibilidad | Ambos | 14 módulos con carpeta propia; IA y ML detrás de una interfaz | 📈 Medido real (estructura) | `docs/modulos.md` + `Musubi.Api/Modules/README.md` | Revisión de la estructura en cada PR |
| RNF-24 Versiones, estándares y documentación | Mantenibilidad | Ambos | CI verde en cada PR; specs con la plantilla | 📈 Medido real | GitHub Actions | `.github/workflows/ci-pr.yml` |
| RNF-25 Pruebas en coincidencias, pedidos y autenticación | Mantenibilidad | Ambos | Cada HU de M-01, M-06 y M-08 con pruebas unitarias, de integración y de seguridad | 🎯 Objetivo (Por medir) | xUnit | Suites por HU |

No aplican al back (viven en `musubi-app:00-03`): RNF-12, RNF-17, RNF-19, RNF-20, RNF-26 y RNF-27.

### NFR técnicos

| NFR | Categoría | Métrica / Umbral | Estado del umbral | Herramienta | Comando / verificación |
|---|---|---|---|---|---|
| Build sin advertencias | Calidad | 0 errores / 0 advertencias (AnalysisMode=All + SonarAnalyzer) | 📈 Medido real (15-Oct-2026) | MSBuild + analizadores | `dotnet build Musubi.slnx -c Release` |
| Vulnerabilidades en dependencias | Seguridad | 0 avisos de NuGetAudit (incluidos los transitivos) | 📈 Medido real | NuGetAudit (`NuGetAuditMode=all`) | `dotnet build` (falla si hay un aviso) |
| SAST | Seguridad | 0 hallazgos con `--config=auto` + las reglas propias | 📈 Medido real | Semgrep 1.181.0 | `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error` |
| Guardarraíles | Calidad | critic PASS; endpoints OK | 📈 Medido real | PowerShell | `pwsh scripts/critic-guardrails.ps1`; `pwsh scripts/check_endpoints.ps1` |
| Cobertura de código | Mantenibilidad | ≥ N % de líneas en el código nuevo | 🎯 Objetivo (Por medir) — 02-06 | coverlet.MTP | `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build --coverlet` (reporte en `TestResults/`, ignorado) |
| Calidad continua | Calidad | Quality Gate de SonarCloud | 🎯 Objetivo (Por medir) — 02-06 | SonarCloud | Pendiente (requiere la organización y el secreto `SONAR_TOKEN`) |
| Mutation score | Mantenibilidad | ≥ 80 % en los servicios de M-01, M-06 y M-08 | 🎯 Objetivo (Por medir) — post-MVP | Stryker.NET (no instalado) | Pendiente |
| Contrato entre repos | Integración | Forma JSON igual a la de los fixtures | 📈 Medido real (ping) | ContractTest | `dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build` |
| Imagen de contenedor | Seguridad | Multi-stage, usuario no root; 0 hallazgos HIGH/CRITICAL | 📈 Build en CI / 🎯 escaneo — 02-05 | Docker + Trivy (pendiente) | Job `docker-build` |
| DAST | Seguridad | 0 alertas altas | 🎯 Objetivo (Por medir) — 02-05 | OWASP ZAP (pendiente) | Pendiente |
| Límites de Kestrel | Rendimiento | Cuerpo ≤ 10 MB; keep-alive de 2 min | 📈 Medido real (configuración) | Kestrel | `appsettings.json` + `ServiceRegistrationTests` |
| Salud del servicio | Salud | `/health` y `/health/ready` → 200; con MongoDB inalcanzable, `database` → Unhealthy (ping con timeout de 5 s) | 📈 Medido real | ASP.NET Health Checks + `ping` de MongoDB | `IntegrationTest/Health/HealthTests.cs`, `UnitTest/Data/DatabaseHealthCheckTests.cs` |
| Versionamiento | Mantenibilidad | La versión del csproj es SemVer y coincide con la última fila de `docs/versionado.md` | 📈 Medido real (20-Oct-2026) | xUnit | `UnitTest/Common/VersioningTests.cs` |
| Integridad de datos | Datos | Esquema validado en el servidor; correo único y consumo de refresh token atómicos con concurrencia; fakes con el mismo comportamiento que MongoDB | 📈 Medido real (21-Oct-2026) | Testcontainers + batería de contrato | Job `database` (`DatabaseTest/`) y `UnitTest/Data/InMemoryAuthRepositoryTests.cs` |

### NFR de los lineamientos de la materia

Checklist completo en [`docs/bitacora/lineamientos-materia.md`](../../../docs/bitacora/lineamientos-materia.md); aquí solo los que tienen umbral medible en el back.

| NFR | Categoría | Métrica / Umbral | Estado del umbral | Herramienta | Comando / verificación |
|---|---|---|---|---|---|
| L-15a Escalabilidad horizontal | Disponibilidad | ≥ 2 instancias sanas detrás del ALB | 🎯 Objetivo (Por medir) — 02-05 | Auto Scaling Group + health check del ALB contra `/health/ready` | `NOTE (02-05)` |
| L-15b Costo cero | Costo | 0 USD fuera de los créditos de AWS; Atlas M0 | 🎯 Objetivo (Por medir) — 02-05 | AWS Budgets (alerta) | `NOTE (02-05)` |
| L-15i Rate limit | Seguridad | 429 + `Retry-After` al superar N peticiones por minuto por IP: `/auth/*` con N = 10 (02-03); global por medir | 📈 Medido real en `/auth/*` (`SecurityTest/Auth/RateLimitTests.cs`) / 🎯 global — 02-04 | `Microsoft.AspNetCore.RateLimiting` | `NOTE (02-04)` |
| L-15i Compresión | Rendimiento | Respuestas JSON comprimidas (Brotli/Gzip) sobre HTTPS | 🎯 Objetivo (Por medir) — 02-04 | Response Compression | `NOTE (02-04)` |
| L-15i Paginación | Rendimiento | Listados con tamaño de página máximo (por medir) | 🎯 Objetivo (Por medir) — 02-02 | Contrato de `docs/endpoints.md` | `NOTE (02-02)` |
| L-09 Caché de BD | Rendimiento | Hit ratio de Redis/Valkey en las lecturas calientes (por medir) | 🎯 Objetivo (Por medir) — 02-02 | Redis/Valkey | `NOTE (02-02)` |
| L-15g Observabilidad | Operación | 100 % de los errores 5xx en CloudWatch Logs con id de correlación | 🎯 Objetivo (Por medir) — 02-06 | CloudWatch Logs | `NOTE (02-06)` |
| L-16b Moderación | Seguridad | 0 imágenes o textos marcados como inapropiados persistidos | 🎯 Objetivo (Por medir) — 02-07 | Rekognition + Comprehend | `NOTE (02-07)` |

Pendientes que no se instalan ahora (decisión de la plantilla inicial): Stryker, NBomber (carga), Pact, RESTler y chaos.

## Contratos
N/A — spec constitucional de umbrales.

## Tests
Los de la columna de verificación. Hoy hay pruebas reales para RNF-02, RNF-13 (límite del cuerpo), RNF-18 (errores genéricos), la salud del servicio y el contrato.

## Criterios
- Cada fila cita una herramienta y un comando o una prueba (sin celdas vacías).
- Las filas "📈 Medido real" se pueden ejecutar hoy y están en verde (DoD de la plantilla inicial, Memoria.md, 15-Oct-2026).
- Las filas "🎯" declaran su spec dueña o "Por definir con el PO".

## Límites
Los umbrales "Por definir con el PO" esperan a que se designe el Product Owner. Los "🎯 Por medir" pasan a "📈" cuando su HU o su fase los implementa.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 20-Oct-2026
- **Detalle:** RNF-01…RNF-27 del MVP filtrados al back, más los NFR técnicos; los pendientes llevan su fase dueña. 20-Oct-2026 (SCRUM-66): readiness contra MongoDB, versionamiento medido y NFR de los lineamientos de la materia con su spec dueña.
