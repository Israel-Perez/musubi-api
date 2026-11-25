# musubi-api

API de **MUSUBI**, la app de compra-venta entre estudiantes de la UTTT. Hecha con **ASP.NET Core / .NET 10**, **MongoDB Atlas** y **Redis**, desplegada en **AWS** y versionada en `api/v1`.

Se desarrolla con **Spec Driven Development (SDD)** y DevSecOps:

- Cada cambio nace de una spec en `specs/` (plantilla `specs/_template.md`) y se verifica con pruebas y guardarraíles en el CI.
- Las reglas no negociables están en `.specify/memory/constitution.md`.

**Enlaces:**

| Qué | Dónde |
|---|---|
| API en producción | <https://api.musubiapp.com.mx> (`GET /api/v1/ping`) |
| App Flutter (repo hermano) | <https://github.com/Israel-Perez/musubi-app> |
| Contrato entre repos (132 endpoints) | [`docs/endpoints.md`](docs/endpoints.md) |
| Historias de usuario ↔ specs ↔ pruebas | [`docs/trazabilidad.md`](docs/trazabilidad.md) |
| Versiones de la API, la app y la infraestructura | [`docs/versionado.md`](docs/versionado.md) |
| Lineamientos de la materia (evaluación 02-Dic-2026) | [`docs/bitacora/lineamientos-materia.md`](docs/bitacora/lineamientos-materia.md) |
| Backlog y documentación funcional | Jira (proyecto `SCRUM`) y Confluence |

## Qué hace

Es un monolito modular, una sola API REST con **14 módulos** (`Musubi.Api/Modules/`, detalle en [`docs/modulos.md`](docs/modulos.md)):

| Módulo | Qué cubre |
|---|---|
| Auth | Registro con correo institucional y código, sesión con JWT + refresh rotativo, logout, recuperar y cambiar la contraseña, eliminar la cuenta |
| Profiles | Perfil, privacidad, intereses, insignias de confianza |
| Listings | Publicar con fotos, editar, pausar, piezas, búsqueda con filtros, vitrina, populares, favoritos, búsquedas guardadas, comparar y "Avísame" |
| PurchaseRequests y Matching | "Se busca", ofertas, coincidencias automáticas |
| Chat | Chat en tiempo real (SignalR), con fotos moderadas, "escribiendo…" y "leído" |
| Orders | Pedidos, pago en efectivo con código o por transferencia (CLABE cifrada), apartar 24 h con QR, recordatorio de la entrega y "Ya llegué" |
| Reputation | Calificaciones, reportes y bloqueos |
| Notifications | Centro de avisos y push con Firebase |
| GenAi | Acuerdo de entrega e ideas de publicación con Gemini |
| Recommendations | "Recomendado para ti" y precio sugerido con ML.NET (regresión por campus, medida con MAE) |
| SellerSupport | Apoyo a vendedores |
| Admin | Moderación de reportes y cuentas, categorías |

Además, todo texto o foto que publica una persona pasa por moderación con AWS (Rekognition, Comprehend y Translate) antes de guardarse.

## Arquitectura

```
App Flutter ──HTTPS──▶ CloudFront (api.musubiapp.com.mx, ACM, TLS 1.2)
                         ├─ /media/*  ──▶ S3 privado (CDN de imágenes, OAC)
                         └─ /api, /hubs ──▶ ALB ──▶ Auto Scaling Group: 2 EC2 t3.micro en 2 zonas
                                                     (Docker, imagen de ECR)
                                                       ├─▶ MongoDB Atlas M0 (base NoSQL de documentos)
                                                       ├─▶ Redis Cloud (caché, revocación de JWT, backplane de SignalR)
                                                       └─▶ SES · Rekognition · Comprehend · Translate · FCM · Gemini
```

- **IaC:** Terraform en [`infra/`](infra/README.md). Los workflows `infra-up` e `infra-down` crean y destruyen todo.
- **Secretos:** en Parameter Store (`/musubi/prod/*`), nunca en el repo.
- **Base de datos:** 34 scripts `mongosh` numerados e idempotentes en [`db/`](db/). Tienen 85 índices (únicos, parciales y TTL) y validación de esquema; el modelo está en [`docs/modelo-datos.md`](docs/modelo-datos.md).

## Seguridad (DevSecOps)

- **Sesión:** JWT HS256 de 15 min y refresh token rotativo con detección de reutilización.
  - El access token se revoca de inmediato con una lista en Redis: logout, suspensión, cambio de contraseña o cuenta eliminada.
  - Contraseñas con PBKDF2-SHA512 de 210 000 iteraciones.
- **Por defecto, todo pide sesión:** la autenticación es explícita en cada controller y se verifica con una prueba.
- **Rate limit:** en `/auth`, subidas, chat y reportes.
- **Errores:** `problem+json` sin detalle interno.
- **Transporte:** HSTS, CORS restringido, TLS 1.2 en CloudFront; el ALB solo acepta a CloudFront.
- **Encabezados:** `nosniff`, `Cross-Origin-Resource-Policy` y `Referrer-Policy` en cada respuesta.
- **Datos sensibles:** la CLABE se cifra con AES-256-GCM. Hay moderación de contenido y validación de entradas.
- **Contenedor:** multi-stage, sin usuario root.
- **CI y CD:** detalle en las tablas de abajo.

## Rendimiento

| Técnica | Dónde |
|---|---|
| Caché | En 3 niveles: datos en Redis (cache-aside), caché de salida de las listas públicas, y ETag/304 junto con la caché de la app |
| Rate limiting | `/auth`, subidas, chat y reportes |
| Middleware | Encabezados de CloudFront, registro por petición, errores, compresión |
| Paginación | Por cursor |
| Índices | 85, en `db/` |
| Serialización | JSON camelCase |
| Compresión | Brotli y Gzip (nivel medido; nunca en `/auth`) |

## Observabilidad

- **Logs:** JSON fuera de Development, una línea por petición, en CloudWatch (`/musubi/prod/api`).
- **Alarmas con aviso por correo (SNS):**
  - errores de la API;
  - 5xx del balanceador;
  - instancias sanas;
  - latencia p95.
- **Dashboard:** `musubi-api`.
- Todo definido en [`infra/stack/observability.tf`](infra/stack/observability.tf).

## Pruebas

Conteos al 23-Nov-2026, todos en verde en el CI:

| Suite | Pruebas | Qué cubre |
|---|---|---|
| `UnitTest` | 995 | Reglas de los servicios, validadores, configuración |
| `IntegrationTest` | 217 | Flujos HTTP completos (WebApplicationFactory) |
| `SecurityTest` | 340 | 401/403, BOLA, JWT, rate limit, CORS, HSTS, encabezados de seguridad, errores, compresión |
| `ContractTest` | 31 | La forma JSON que consume la app |
| `DatabaseTest` | 195 | Índices, esquema y repositorios contra MongoDB real (Testcontainers) |
| `PerformanceTest` | 4 | p95 bajo carga concurrente |
| `MutationTest` | semanal | Stryker.NET (workflow `mutation.yml`) |

Cada fila de `docs/endpoints.md` tiene prueba unitaria, de integración y de seguridad. Lo exige `scripts/check_endpoint_tests.ps1` en el CI, así que un endpoint nuevo sin sus pruebas no entra.

## CI y CD

| Job (`ci-pr.yml`, en cada PR y push a `main`) | Qué hace |
|---|---|
| `build` | Build Release sin errores ni advertencias (analizadores como error) |
| `unit`, `integration`, `security` | Suites y el guard de pruebas por endpoint |
| `contract`, `database`, `performance` | Suites |
| `critic` | Guardarraíles: sin secretos, sin `TODO`, auth explícita, sin `catch` en controllers |
| `endpoints` | El catálogo y el código coinciden |
| `semgrep` | SAST (`--config=auto` + reglas propias) |
| `docker-build` | Imagen y escaneo con **Trivy** |
| `gitleaks` | Secretos en el historial completo de Git |
| `dast` (`dast.yml`) | **DAST con OWASP ZAP**: escanea la API levantada en el job (modo Production, con y sin sesión) en cada PR, al fusionar y cada semana. Falla con riesgo Alto |
| `terraform` | `fmt` y `validate` |

| CD | Qué hace |
|---|---|
| `infra-up` | OIDC hacia AWS sin llaves, imagen a ECR (escaneo al subir, tags inmutables), scripts de la base, `terraform apply`, reemplazo de instancias, prueba de humo y tag `api-vX.Y.Z` |
| `infra-down` | Destruye la infraestructura (costo cero fuera de uso) |

Las actions están fijadas por SHA y Dependabot mantiene al día NuGet, actions y Docker.

## Puesta en marcha

```powershell
dotnet restore Musubi.slnx
dotnet run --project Musubi.Api          # Development: en memoria (sin MongoDB), http://localhost:5080
# /api/v1/ping  ·  /health  ·  /health/ready  ·  /scalar (referencia interactiva, solo Development)
```

- **MongoDB local o Atlas:** ver [`deploy/README.md`](deploy/README.md).
- **Llave JWT fija:** `dotnet user-secrets --project Musubi.Api set "Jwt:SigningKey" "<base64 de 32+ bytes>"`. Fuera de Development la API no arranca sin ella.
- **Secretos:** solo en user-secrets o en Parameter Store. La tabla de claves está en [`01-04`](specs/phase-01-foundation/01-04-config-reference/spec.md).

**Comandos de calidad (los mismos del CI):**

```powershell
dotnet build Musubi.slnx -c Release
dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build   # igual: IntegrationTest, SecurityTest, ContractTest
pwsh scripts/check_endpoint_tests.ps1 -Suite UnitTest                   # e IntegrationTest / SecurityTest después de su suite
pwsh scripts/critic-guardrails.ps1
pwsh scripts/check_endpoints.ps1
semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error
```

## Estructura

```
Musubi.Api/        Program.cs, Platform/ (auth, caché, hosting, datos, medios, moderación, tiempo real), Modules/ (14)
UnitTest/ IntegrationTest/ SecurityTest/ ContractTest/ DatabaseTest/ PerformanceTest/ MutationTest/ TestShared/
db/                scripts mongosh numerados e idempotentes
infra/             Terraform (bootstrap y stack)
specs/             SDD: fases 00–02 y mNN-<modulo>/HU-NN-*/
docs/              endpoints, modelo de datos, trazabilidad, módulos, errores, versionado, MCP, guía del equipo
scripts/           critic, endpoints, pruebas por endpoint, secciones compartidas
.claude/ .opencode/  agentes de IA (3) y skills (10)
```

## IA en el desarrollo

- **Agentes:** 3 sub-agentes en `.claude/agents/`.
  - `slice-scaffolder`: arma el esqueleto de una HU.
  - `security-reviewer`: revisa contra OWASP antes de cada push. Es de solo lectura.
  - `traceability-clerk`: cuida spec ↔ código ↔ prueba ↔ Jira. Es de solo lectura.
- **Skills:** 10 en `.claude/skills/`.
- **MCP:** Atlassian (Jira y Confluence) y AWS ([`docs/mcp.md`](docs/mcp.md)).
- **Memoria:** el aprendizaje del desarrollo queda en [`Memoria.md`](Memoria.md); las reglas permanentes, en `AGENTS.md`.

## Flujo SDD y ramas

¿Eres nuevo en el equipo? Lee [`docs/guia-equipo.md`](docs/guia-equipo.md).

1. Spec de la HU (`specs/mNN-<modulo>/HU-NN-<slug>/`).
2. Rama `feature/SCRUM-NN-slug`.
3. Implementación y pruebas.
4. `security-reviewer`.
5. PR con la clave Jira y el checklist.

`main` está protegida (ruleset "Main Proteccion", spec [`02-08`](specs/phase-02-architecture/02-08-ci-cd/spec.md)): no acepta force push, exige PR con 1 aprobación y los checks del CI en verde con la rama al día (incluidos `gitleaks` y `zap`).

## Créditos

Las prácticas de SDD, DevSecOps y guardarraíles se adaptaron del repositorio de referencia **WebAPIDevSecOpsScallingSDD**, compartido en la materia de Aplicaciones Móviles. Se copió la forma de trabajar, no el dominio ni el stack.
