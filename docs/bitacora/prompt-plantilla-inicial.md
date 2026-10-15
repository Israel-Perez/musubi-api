# Prompt para Claude Code — Plantillas iniciales de los repositorios de MUSUBI (back y front)

## 0. Datos a completar antes de usar este prompt

- **Repo de referencia (ruta local):** `C:\Users\adani\source\repos/WebAPIDevSecOpsScallingSDD`
- **Plataforma de los repos y CI:** GitHub (GitHub Actions)
- **Repos a crear:** `musubi-api` (back) y `musubi-app` (front) ← cámbialos si usan otros nombres
- **Cuenta u organización de GitHub:** `https://github.com/Israel-Perez` ← para los enlaces entre repos
- **applicationId de Android:** pregúntamelo antes de crear la app (no se puede cambiar después de publicar en Google Play)

## 1. Rol y objetivo

Eres el arquitecto técnico del equipo de MUSUBI. Trabajas desde una carpeta contenedora `musubi/` (que **no** es repositorio). Dentro vas a crear **dos repositorios hermanos**, cada uno con su propio `git init -b main`:

- `musubi-api/`: el **back** (API en .NET 10).
- `musubi-app/`: el **front** (app móvil en Flutter).

En los dos vas a copiar las **prácticas** del repo de referencia `edelomeza/WebAPIDevSecOpsScallingSDD`: Spec Driven Development (SDD), DevSecOps y calidad verificable por comando. Cada repo debe funcionar solo: con Claude Code, con su CI y sin depender de archivos locales del otro.

Copias la **forma de trabajar**, no el dominio ni el stack de la referencia: MUSUBI tiene los suyos (sección 4). Si algo de este prompt choca con la referencia, manda este prompt: es la adaptación acordada para MUSUBI.

Al terminar, cada repo debe compilar, pasar sus pruebas y guardarraíles, y quedar listo para que el equipo implemente historias de usuario (HU) con SDD.

## 2. Cómo vas a trabajar

1. **Lee la referencia completa antes de proponer nada** (no supongas): `README.md`, `AGENTS.md`, `Memoria.md`, `.specify/memory/constitution.md`, `specs/_template.md`, todas las specs de `specs/phase-00-constitution/` y `specs/phase-01-foundation/`, `Directory.Build.props`, `nuget.config`, `.gitignore`, `.dockerignore`, `.github/`, `scripts/`, `.semgrep/`, `docs/endpoints.md` y `.opencode/` (agentes y skills).
2. **Entrégame un plan y espera mi aprobación antes de crear archivos.** El plan incluye:
   - tabla "práctica de la referencia → cómo queda en `musubi-api` → cómo queda en `musubi-app`";
   - lo que NO vas a copiar y por qué;
   - árbol de carpetas final de cada repo;
   - paquetes de Flutter que propones, con su justificación (sección 5.4);
   - solo las dudas que no puedas resolver con este prompt o con la referencia.
3. Implementa primero el **back** y después el **front**, porque el front consume el contrato de la API. Trabaja por bloques y, al cerrar cada bloque, verifica (build/tests/análisis) antes de seguir.
4. **Verifica empíricamente** (lección #1 de la referencia): consulta versiones reales con `dotnet --version`, `flutter --version`, `dart pub outdated`, etc. No inventes versiones, rutas ni nombres.
5. **No hagas commit ni push** en ningún repo. Al final entrega el resumen y los mensajes de commit propuestos (sección 7).

## 3. Qué copiar y qué no

**Copiar, adaptado a MUSUBI (en los dos repos, salvo que se indique):**

- Constitución en `.specify/memory/constitution.md` ("ante conflicto, manda la constitución").
- `specs/_template.md` con las 7 secciones (Contexto, Requisitos, Diseño, Contratos, Tests, Criterios, Límites) + "Aprobación y Control de Cambios" + reglas de opcionalidad. Cada spec vive en su carpeta con `spec.md`, `plan.md` y `task.md`.
- Fase 00 (principios y stack, gobernanza SDD con DoD, NFR, lecciones aprendidas) y fase 01 (fundación) como specs reales, en estado "🚧 Borrador con evidencia" (nadie firma hasta que el equipo designe revisores).
- `AGENTS.md` como manual operativo (forma de trabajo, memoria, límites, verificación, convenciones, seguridad) y `Memoria.md` como bitácora viva, con las mismas reglas de actualización.
- `NOTE (ID)` en lugar de `TODO` para alcance diferido, trazable en 4 capas: código, `spec.md`, `task.md` y `Memoria.md`.
- Catálogos canónicos en `docs/` que las specs enlazan y nunca duplican, más guardarraíles que detectan drift.
- Análisis estático estricto, con las advertencias tratadas como errores:
  - back: `Directory.Build.props` (AnalysisMode=All, TreatWarningsAsErrors, EnforceCodeStyleInBuild, Nullable, NuGetAudit, SonarAnalyzer.CSharp) y `nuget.config` con Package Source Mapping;
  - front: `analysis_options.yaml` estricto y `flutter analyze --fatal-infos --fatal-warnings`.
- `.gitignore` y `.dockerignore` defensivos. Secretos siempre fuera de Git.
- Suites de pruebas separadas y verdes antes de integrar (detalle por repo en 5.3 y 5.4).
- `scripts/critic-guardrails.ps1` con checks bloqueantes sobre el diff, compatible con PowerShell 5.1 y `pwsh`, en cada repo. `scripts/check_endpoints.ps1` solo en el back.
- Semgrep con reglas propias en `.semgrep/semgrep.yaml`.
- Plantilla de PR con checklist, CI en GitHub Actions con actions fijadas por SHA y `dependabot.yml`.
- Sub-agentes y skills con la convención híbrida de la referencia (el agente define rol, permisos y criterio de "done"; las reglas viven en skills y el agente solo las enlaza), **portados a Claude Code** (bloque 5.5).
- Umbrales aún no medidos: "🎯 Objetivo (Por medir)" con su fase dueña, como en la referencia.

**No copiar:**

- El dominio de la referencia (clientes, empleados, ventas, saga, facturas) ni su convención legacy de nombres (`str/int/dec/dte/bln`, `Ven*`, `Cli*`, JSON con prefijos). MUSUBI usa código en inglés y JSON camelCase; los documentos y specs van en español.
- Tecnologías que MUSUBI no usa: SQL Server, Redis, MassTransit/SQS, CloudFormation. Si alguna se vuelve necesaria, entra por la regla de excepción de 00-01.
- `.opencode/` tal cual: se porta a `.claude/`.
- Stryker, NBomber, Pact, RESTler y chaos: no se instalan ahora; quedan en la tabla NFR y en `Memoria.md` como pendientes con fase dueña.
- Skills de fases de la referencia (03-…09-): se crean cuando MUSUBI llegue a esa necesidad.
- Nada de .NET en el front ni de Flutter en el back.

## 4. Contexto de MUSUBI

**Proyecto.** App Android de compra-venta entre estudiantes de la UTTT (Universidad Tecnológica de Tula-Tepeji). Conecta la **oferta** (productos publicados) con la **demanda** (solicitudes de compra "busco…"), con coincidencias automáticas, chat, pedidos con pago al momento de la entrega y Mercado Pago en modo de pruebas, reputación, IA generativa para resumir acuerdos y recomendaciones con ML.

**Calendario del MVP.** 5 sprints de 2 semanas, del 28 sep al 4 dic 2026. Prueba cerrada en Google Play desde el 9 nov; publicación y entrega final el 4 dic 2026.

| Sprint | Fechas | Enfoque |
|---|---|---|
| S1 | 28 sep – 9 oct | Base técnica, registro, verificación con correo institucional, perfil |
| S2 | 12 – 23 oct | Publicaciones, búsqueda, chat, privacidad y eliminación de cuenta |
| S3 | 26 oct – 6 nov | Solicitudes, coincidencias, notificaciones, pedidos, reputación (hito 6 nov) |
| S4 | 9 – 20 nov | Búsquedas guardadas, IA de acuerdos, Mercado Pago, entregas en campus |
| S5 | 23 nov – 4 dic | Recomendaciones, apoyo a vendedores, administración, publicación |

**Equipo.** Start-Up 10IDGS-G3: 4 integrantes (roles Scrum por definir). Docentes de Gestión de Proyectos, Integradora y Aplicaciones Móviles. *(Nombres omitidos en la versión pública del repo; viven en Jira y Confluence.)*

**Stack.**

| Capa | Repo | Tecnología |
|---|---|---|
| App móvil | front | Flutter (Dart, canal stable), Android primero (targetSdk 36) |
| API | back | ASP.NET Core / .NET 10 (C#), API versionada `api/v1/...` |
| Datos | back | PostgreSQL + EF Core (Npgsql); InMemory solo para pruebas |
| Tiempo real | ambos | SignalR: hub en el back y cliente en el front (chat y avisos dentro de la app) |
| Notificaciones y monitoreo | ambos | Firebase Cloud Messaging (el back envía, el front recibe) y Firebase Crashlytics (front) |
| IA generativa | back | API de un modelo de lenguaje detrás de una interfaz; proveedor intercambiable |
| Machine Learning | back | ML.NET (recomendaciones, fase posterior) |
| Contenedores y nube | back | Docker; AWS para API, base de datos administrada e imágenes |
| Código y CI | ambos | GitHub + GitHub Actions + Dependabot |
| Calidad y seguridad | ambos | SonarCloud y Semgrep; Trivy y OWASP ZAP en fases posteriores (back). Referencias: OWASP API Security Top 10 – 2023 (back) y OWASP MASVS (front) |
| Gestión | ambos | Jira (proyecto `SCRUM`) para backlog y sprints; Confluence para documentación |

**Módulos** (cada uno es una épica en Jira):
M-01 Autenticación y usuarios (SCRUM-5) · M-02 Perfil (SCRUM-6) · M-03 Publicaciones (SCRUM-7) · M-04 Marketplace (SCRUM-8) · M-05 Solicitudes de compra (SCRUM-9) · M-06 Coincidencias (SCRUM-10) · M-07 Chat (SCRUM-11) · M-08 Pedidos, pagos y entregas (SCRUM-12) · M-09 Notificaciones (SCRUM-13) · M-10 IA generativa (SCRUM-14) · M-11 Recomendaciones ML (SCRUM-15) · M-12 Apoyo a vendedores (SCRUM-16) · M-13 Reputación (SCRUM-17) · M-14 Administración (SCRUM-18).

**Requerimientos no funcionales** (base de la spec 00-03 de cada repo; los marcados "por definir" quedan como "🎯 Objetivo (Por definir con el PO)"):

- Seguridad: RNF-01 credenciales cifradas · RNF-02 comunicación cifrada · RNF-03 solo el dueño modifica sus publicaciones, solicitudes u ofertas; solo el admin modera · RNF-04 moderación registrada.
- Privacidad: RNF-05 datos mínimos y aviso de privacidad · RNF-06 a la IA solo se envía la conversación si un participante lo pide, con datos mínimos · RNF-07 búsquedas e historial privados (uso solo agregado) · RNF-08 eliminación de cuenta y datos.
- Rendimiento: RNF-09 búsquedas en tiempo aceptable (s por definir) · RNF-10 chat casi en tiempo real (s por definir) · RNF-11 coincidencias en segundo plano, sin bloquear · RNF-12 indicador mientras se genera el resumen de IA · RNF-13 fotos optimizadas antes de subir (tamaño por definir).
- Disponibilidad: RNF-14 disponible durante el piloto (% por definir) · RNF-15 si la IA o las recomendaciones fallan, búsqueda, chat y pedidos siguen funcionando · RNF-16 respaldos periódicos de la BD.
- Usabilidad: RNF-17 publicar en pocos pasos · RNF-18 interfaz en español con errores claros · RNF-19 distinguir visualmente productos de solicitudes · RNF-20 legibilidad y contraste.
- Escalabilidad: RNF-21 agregar más universidades sin rediseño · RNF-22 soportar la carga del piloto (usuarios por definir).
- Mantenibilidad: RNF-23 organización por los 14 módulos; IA y ML desacoplados · RNF-24 control de versiones, estándares y documentación · RNF-25 pruebas automatizadas en coincidencias, pedidos y autenticación.
- Compatibilidad: RNF-26 plataformas definidas por el equipo (Android primero) · RNF-27 distintos tamaños de pantalla.

**Historias de usuario** (para `docs/trazabilidad.md` de cada repo; la fuente de verdad del backlog es Jira):

| HU | Jira | Sprint | Módulo | Título |
|---|---|---|---|---|
| HU-01 | SCRUM-19 | S1 | M-01 | Crear cuenta de estudiante |
| HU-02 | SCRUM-20 | S1 | M-01 | Iniciar y cerrar sesión |
| HU-03 | SCRUM-21 | S1 | M-01 | Validar pertenencia con correo institucional |
| HU-04 | SCRUM-22 | S1 | M-02 | Editar mi perfil |
| HU-05 | SCRUM-34 | S3 | M-02 | Consultar perfil y reputación de otro usuario |
| HU-06 | SCRUM-23 | S2 | M-03 | Publicar un producto |
| HU-07 | SCRUM-24 | S2 | M-03 | Editar o eliminar mis publicaciones |
| HU-08 | SCRUM-25 | S2 | M-03 | Consultar el estado de mis productos |
| HU-09 | SCRUM-26 | S2 | M-04 | Buscar productos por palabra clave |
| HU-10 | SCRUM-27 | S2 | M-04 | Filtrar por categoría y precio |
| HU-11 | SCRUM-28 | S2 | M-04 | Ver el detalle de un producto |
| HU-12 | SCRUM-51 | S4 | M-04 | Guardar una búsqueda sin resultados |
| HU-13 | SCRUM-35 | S3 | M-05 | Publicar una necesidad de compra |
| HU-14 | SCRUM-36 | S3 | M-05 | Editar, cerrar o cancelar mi solicitud |
| HU-15 | SCRUM-37 | S3 | M-05 | Explorar solicitudes publicadas |
| HU-16 | SCRUM-38 | S3 | M-05 | Responder "Yo tengo este producto" |
| HU-17 | SCRUM-39 | S3 | M-05 | Ver y elegir ofertas recibidas |
| HU-18 | SCRUM-40 | S3 | M-06 | Ver coincidencias al publicar una solicitud |
| HU-19 | SCRUM-41 | S3 | M-06 | Aviso al vendedor de solicitud compatible |
| HU-20 | SCRUM-29 | S2 | M-07 | Iniciar una conversación con el vendedor |
| HU-21 | SCRUM-30 | S2 | M-07 | Ver el contexto de la conversación |
| HU-22 | SCRUM-31 | S2 | M-07 | Bandeja de conversaciones |
| HU-23 | SCRUM-42 | S3 | M-08 | Confirmar la compra de un producto |
| HU-24 | SCRUM-43 | S3 | M-08 | Aceptar y actualizar mis pedidos |
| HU-25 | SCRUM-44 | S3 | M-08 | Consultar mis pedidos |
| HU-26 | SCRUM-45 | S3 | M-08 | Consultar mi historial de ventas |
| HU-27 | SCRUM-46 | S3 | M-13 | Calificar al vendedor al terminar un pedido |
| HU-28 | SCRUM-57 | S5 | M-13 | Reportar contenido o usuarios sospechosos |
| HU-29 | SCRUM-47 | S3 | M-09 | Aviso de producto buscado disponible |
| HU-30 | SCRUM-48 | S3 | M-09 | Aviso de oferta en mi solicitud |
| HU-31 | SCRUM-49 | S3 | M-09 | Avisos de mensajes y cambios de estado |
| HU-32 | SCRUM-52 | S4 | M-10 | Pedir un resumen de la conversación con IA |
| HU-33 | SCRUM-53 | S4 | M-10 | La IA identifica los datos del acuerdo |
| HU-34 | SCRUM-54 | S4 | M-10 | Revisar y confirmar la propuesta de la IA |
| HU-35 | SCRUM-58 | S5 | M-11 | Ver productos recomendados |
| HU-36 | SCRUM-59 | S5 | M-12 | Ver mis productos con baja rotación |
| HU-37 | SCRUM-60 | S5 | M-12 | Recibir sugerencias para vender |
| HU-38 | SCRUM-61 | S5 | M-12 | Saber si hay demanda para mis productos |
| HU-39 | SCRUM-62 | S5 | M-14 | Revisar reportes y moderar contenido |
| HU-40 | SCRUM-63 | S5 | M-14 | Suspender y reactivar cuentas |
| HU-41 | SCRUM-64 | S5 | M-14 | Gestionar categorías |
| HU-42 | SCRUM-65 | Post-MVP | M-14 | Consultar indicadores de uso |
| HU-43 | SCRUM-55 | S4 | M-08 | Pagar un pedido con Mercado Pago |
| HU-44 | SCRUM-56 | S4 | M-08 | Coordinar la entrega en un punto del campus |
| HU-45 | SCRUM-50 | S3 | M-13 | Calificar al comprador al terminar un pedido |
| HU-46 | SCRUM-32 | S2 | M-01 | Eliminar mi cuenta y mis datos |
| HU-47 | SCRUM-33 | S2 | M-01 | Aceptar aviso de privacidad y términos de uso |

## 5. Qué construir

### 5.1 Estructura

```
musubi/                          # carpeta contenedora (no es repo)
├── musubi-api/                  # repo back
│   ├── AGENTS.md  CLAUDE.md  Memoria.md  README.md
│   ├── .specify/memory/constitution.md
│   ├── specs/                   # README.md, _template.md, phase-00-constitution/, phase-01-foundation/
│   ├── docs/
│   │   ├── endpoints.md         # contrato canónico de la API (lo consume el front)
│   │   └── trazabilidad.md
│   ├── Musubi.slnx  Directory.Build.props  nuget.config  Dockerfile
│   ├── Musubi.Api/  UnitTest/  IntegrationTest/  SecurityTest/  ContractTest/  DatabaseTest/
│   ├── scripts/                 # critic-guardrails.ps1, check_endpoints.ps1
│   ├── deploy/                  # docker-compose.local.yml con PostgreSQL + README
│   ├── .semgrep/  .claude/agents/  .claude/skills/
│   ├── .github/                 # workflows/ci-pr.yml, pull_request_template.md, dependabot.yml
│   └── .gitignore  .dockerignore  .gitattributes  .editorconfig
└── musubi-app/                  # repo front
    ├── AGENTS.md  CLAUDE.md  Memoria.md  README.md
    ├── .specify/memory/constitution.md
    ├── specs/                   # README.md, _template.md, phase-00-constitution/, phase-01-foundation/
    ├── docs/trazabilidad.md
    ├── lib/                     # app/, core/, features/<modulo>/
    ├── test/  integration_test/  android/
    ├── pubspec.yaml  analysis_options.yaml
    ├── config/                  # *.example.json versionados; los reales, ignorados
    ├── scripts/                 # critic-guardrails.ps1
    ├── .semgrep/  .claude/agents/  .claude/skills/
    ├── .github/                 # workflows/ci-pr.yml, pull_request_template.md, dependabot.yml
    └── .gitignore  .gitattributes  .editorconfig
```

### 5.2 Gobernanza SDD (en los dos repos)

- **Cada repo es autosuficiente:** su constitución, su plantilla, sus specs 00 y 01, su `AGENTS.md`, su `Memoria.md` y su `README.md`.
- **Secciones compartidas:** propósito, principios, gobernanza SDD, convención de IDs y convenciones de Jira, ramas y commits van **iguales en los dos repos**. Márcalas con `<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->`. Stack, arquitectura, estándares, pruebas y CI son propios de cada repo.
- **Constitución:** principios no negociables con consecuencia práctica: seguridad primero, privacidad por diseño (RNF-05 a 08), IA y ML desacoplados y con degradación controlada (RNF-15, 23), multi-comunidad (RNF-21), trabajo pesado en segundo plano (RNF-11), empírico antes que teórico y documentar límites. Arquitectura organizada por los módulos M-01…M-14 en los dos repos.
- **Contrato entre repos:** el contrato de la API es canónico en `musubi-api/docs/endpoints.md` (y el OpenAPI que expone la API en Dev). El front nunca lo duplica: sus specs enlazan a las filas del catálogo del back.
- **Specs de HU:** usan el mismo ID y la misma carpeta en los dos repos, `specs/mNN-<modulo>/HU-NN-<slug>/`. Cada una describe su lado y lleva en el encabezado `**Contraparte:**` con el enlace a la spec del otro repo. Se crean al iniciar el sprint de cada HU.
- **`specs/README.md` (roadmap):**
  - back: fases 00 y 01 ahora; fase 02 (arquitectura y modelo de datos) después; luego una carpeta por módulo;
  - front: fases 00 y 01 ahora; fase 02 (arquitectura de la app: navegación, estado, cliente de API, tema y componentes base) después; luego una carpeta por módulo;
  - IDs: `NN-NN` para specs técnicas y `HU-NN` para funcionales. Para citar una spec del otro repo, antepón el repo (`musubi-api:02-01`). Esos mismos IDs se usan en los `NOTE (…)`.
- **`specs/_template.md`:** igual que la referencia, más una línea de encabezado `**HU / Jira / Sprint / Módulo / Contraparte:**` para specs funcionales (`N/A` en las técnicas).
- **00-01** principios y stack del repo + regla de excepción.
- **00-02** gobernanza y DoD. El revisor es 1 integrante distinto del autor. Una spec solo queda firmada si el usuario indica el revisor; si no, queda en "Borrador con evidencia".
- **00-03** NFR: cada repo toma los RNF que le aplican (algunos aplican a los dos; márcalo) con las columnas de la referencia (NFR, categoría, métrica/umbral, estado, herramienta, comando), más los NFR técnicos que apliquen (build/análisis sin advertencias, cobertura, mutation como pendiente).
- **00-04** lecciones aprendidas: las reglas de la referencia que aplican (evidencia: "Referencia WebAPIDevSecOpsScallingSDD, oct 2026") más las propias de MUSUBI.
  - back: secretos solo en archivos ignorados o user-secrets, nunca en appsettings versionados;
  - front: todo lo que va dentro del APK es público, así que no se meten secretos en el código, en assets ni en `--dart-define`; las llaves privadas (IA, Mercado Pago, Firebase Admin) viven solo en el back.
- **`README.md`** de cada repo: setup, comandos, estructura, flujo SDD, CI, protección de rama recomendada (sección 5.6), enlace al otro repo y crédito al repo de referencia.

### 5.3 Back — `musubi-api`

- **Solución:** `Musubi.slnx` (formato `.slnx`) en la raíz, con `Musubi.Api/`, `UnitTest/`, `IntegrationTest/`, `SecurityTest/`, `ContractTest/` (esqueleto) y `DatabaseTest/` (esqueleto), igual que la referencia.
- **`Program.cs`** como en las specs 01-01 a 01-03 de la referencia:
  - usings explícitos y `public partial class Program`;
  - registro y pipeline en métodos de extensión: ForwardedHeaders → HSTS fuera de Dev → HttpsRedirection → CORS restringido por configuración → OpenAPI + Scalar solo en Dev → Authorization → Controllers;
  - límites de Kestrel en configuración y handler de excepciones JSON genérico fuera de Dev;
  - endpoints `/health` (liveness), `/health/ready` (readiness) y `GET /api/v1/ping` con Asp.Versioning, todos con su fila en `docs/endpoints.md`.
- **Datos:** `AppDbContext` registrado pero sin entidades, con la bandera `UseInMemoryDatabase` y `ConnectionStrings:Default` como placeholder. El modelo nace en la spec 02-01; déjalo como `NOTE (02-01)`.
- **Configuración:** `appsettings.json`, `appsettings.Example.json` y `appsettings.Production.json` solo con placeholders. Secretos locales en archivos ignorados o en user-secrets.
- **Pruebas reales:**
  - Unit: build smoke, appsettings sin secretos, registro de servicios.
  - Integration: health y middleware con WebApplicationFactory.
  - Security: al menos HSTS solo fuera de Dev y CORS restringido.
- **Contenedores:** `Dockerfile` multi-stage (SDK → runtime aspnet, usuario no root) y `deploy/docker-compose.local.yml` con PostgreSQL (verifica la versión de la imagen).
- **`scripts/critic-guardrails.ps1`** con checks bloqueantes sobre el diff contra `origin/main`. Si no existe `origin/main` (repo nuevo), analiza todos los archivos versionados.
  1. `TODO` en código o configuración.
  2. Secretos: `password|secret` en DTOs y connection strings con contraseña en appsettings versionados.
  3. Controller nuevo sin `[Authorize]`, `[AllowAnonymous]` o policy explícita.
  4. `catch` en `Controllers/`.
- **`scripts/check_endpoints.ps1`:** toda ruta de los controllers tiene su fila en `docs/endpoints.md`, y viceversa.
- **`.semgrep/semgrep.yaml`:** al menos dos reglas reales para C# (no un archivo vacío).
- **CI — `.github/workflows/ci-pr.yml`:**
  - misma estructura que la referencia: jobs `build`, `unit`, `security`, `integration`, `critic`, `endpoints`, `contract` y `semgrep`, con `timeout-minutes`;
  - actions fijadas por SHA, con un comentario de la versión;
  - se ejecuta en `pull_request` y `push` a `main`;
  - valida el YAML localmente.
- **`dependabot.yml`:** ecosistemas `nuget` y `github-actions`.
- **Plantilla de PR:** el checklist de la referencia adaptado:
  - clave Jira en el título;
  - build 0/0 y tests verdes;
  - sin secretos;
  - sin `TODO` (solo `NOTE (ID)`);
  - auth explícita y fila en `docs/endpoints.md` para cada endpoint nuevo;
  - spec, plan y task conciliados, y trazabilidad actualizada;
  - `Memoria.md` actualizada.

### 5.4 Front — `musubi-app`

- **Creación:**
  - verifica `flutter --version` y usa el canal stable;
  - pregúntame el applicationId **antes** de `flutter create`, porque `--org` lo define;
  - usa `--platforms android` y el nombre de proyecto `musubi_app`;
  - confirma que el `targetSdk` resultante sea 36 (Google Play lo exige a apps nuevas desde el 31 ago 2026); si no, fíjalo en `android/app/build.gradle.kts`.
- **Estructura por módulos (feature-first):** `lib/features/<modulo>/` con capas `data/`, `domain/` y `presentation/`, y `lib/core/` para configuración, red, errores y tema. Explica la convención en un README; no crees carpetas vacías.
- **Paquetes:** propónlos en el plan con su justificación y espera mi decisión. Evalúa licencia, mantenimiento y seguridad (regla de 00-01). Instala ahora solo los que la fundación usa:
  - gestor de estado (p. ej. Riverpod o Bloc);
  - navegación (p. ej. go_router);
  - cliente HTTP (p. ej. dio).
- **Lo que se agrega después,** como `NOTE` con su HU dueña:
  - almacenamiento seguro del token con `flutter_secure_storage` → `NOTE (HU-02)`;
  - cliente de SignalR → `NOTE (HU-20)`;
  - Firebase con `flutterfire configure`: Messaging → `NOTE (HU-31)` y Crashlytics → `NOTE (02-01)`.
- **Configuración por ambiente:** con `--dart-define-from-file=config/<ambiente>.json`.
  - Versiona `config/*.example.json` e ignora los archivos reales.
  - Esos valores quedan dentro del APK y son públicos: solo la URL base de la API y banderas.
  - En dev la URL apunta al emulador (`http://10.0.2.2:<puerto>`); fuera de dev, HTTPS obligatorio (RNF-02).
- **Primera conexión con el back:** un repositorio detrás de una interfaz que consume `GET /api/v1/ping` (fila del catálogo del back), con un fake para pruebas, siguiendo el patrón de `deferred-scope-fakes`. La pantalla inicial "Musubi" muestra el estado sin romperse si la API no responde.
- **Calidad:**
  - `analysis_options.yaml` estricto (`strict-casts`, `strict-inference`, `strict-raw-types` y un set de lints reconocido);
  - `dart format`;
  - `flutter analyze --fatal-infos --fatal-warnings` sin observaciones.
- **Pruebas:**
  - unitarias del repositorio y del estado;
  - widget test de humo de la pantalla inicial;
  - `integration_test/` como esqueleto.
- **`scripts/critic-guardrails.ps1`** con checks bloqueantes sobre el diff contra `origin/main`, con el mismo respaldo si no existe:
  1. `TODO` en `lib/`, `test/` o `integration_test/`.
  2. Secretos en el código o en el CI: llaves privadas, tokens con prefijos conocidos y `--dart-define` con nombres SECRET/KEY/TOKEN.
  3. `http://` en `lib/` fuera de la configuración de dev.
  4. `catch` vacío o que se trague la excepción.
- **`.semgrep/semgrep.yaml`:** al menos dos reglas reales. Si una regla no aplica a Dart, usa `languages: [generic]` o regex.
- **CI — `.github/workflows/ci-pr.yml`:**
  - actions fijadas por SHA;
  - Flutter con versión fijada;
  - jobs `analyze` (format + analyze), `test` (con cobertura como artefacto), `critic` y `semgrep`;
  - un job `build-apk` (debug, con Java 17) solo en `push` a `main`, para ahorrar minutos;
  - valida el YAML localmente.
- **`dependabot.yml`:** ecosistemas `pub` y `github-actions`.
- **Plantilla de PR:**
  - clave Jira en el título;
  - format, analyze y tests verdes;
  - sin secretos;
  - sin `TODO`;
  - endpoints consumidos enlazados al catálogo del back (o fake con `NOTE` si el endpoint aún no existe);
  - spec, plan y task conciliados, y trazabilidad actualizada;
  - `Memoria.md` actualizada.

### 5.5 Claude Code: instrucciones, sub-agentes y skills (en cada repo)

- **`CLAUDE.md` corto:** `@AGENTS.md` y "lee `Memoria.md` al empezar". `AGENTS.md` sigue siendo la fuente.
- **`.claude/agents/`** (frontmatter `name`, `description`, `tools`):
  - `slice-scaffolder`: esqueleto compilable de la parte de una HU que toca a su repo. En el back: entidad → DTO → validador → controller → servicio → tests. En el front: modelo → repositorio (fake si el endpoint aún no existe) → estado → pantalla → tests.
  - `security-reviewer`: gate pre-push que solo critica el diff y no edita (tools de solo lectura + `git diff`/`git log`). Revisa contra OWASP API Security Top 10 en el back y OWASP MASVS en el front.
  - `traceability-clerk`: verifica spec ↔ código ↔ test ↔ Jira contra `docs/trazabilidad.md` y reporta drift sin editar.
- **`.claude/skills/<nombre>/SKILL.md`** (frontmatter `name`, `description`), con la estructura interna de las skills de la referencia (Propósito, Cuándo usarla, Precondiciones, Pasos, Checklist, Criterios de done, Límites/trampas, Referencias). Porta y adapta, no copies literal:
  - en los dos repos: `spec-first-writing`, `vertical-slice` (versión de cada repo), `traceability` (4 capas + clave Jira), `deferred-scope-fakes`, `empirical-verification`, `critic-guardrails`, `powershell-quirks` y `testing-strategy`;
  - solo en el back: `drift-guards` y `docker-multistage`;
  - solo en el front: `api-contract` (cómo consumir una fila del catálogo del back y cuándo usar fake).

### 5.6 Trazabilidad con Jira y GitHub

- El backlog vive en Jira (`SCRUM`) y la documentación funcional en Confluence; los repos enlazan, no duplican.
- **`docs/trazabilidad.md`** en cada repo, precargado con las 47 HU de la sección 4.
  - Columnas: HU, Jira, Sprint, Módulo, Spec (este repo), Contraparte, Tests, Estado.
  - Spec, Contraparte y Tests quedan en "—" hasta que existan.
- **Ramas:** `feature/SCRUM-NN-slug` y `fix/SCRUM-NN-slug`, iguales en los dos repos.
- **Commits y títulos de PR:** empiezan con la clave, al estilo de la referencia: `SCRUM-19 HU-01 Crear cuenta: <resumen> (Unit N/Integration N/Security N, critic PASS)`.
- **HU que toca los dos repos:** un PR por repo con la misma clave; el PR del front enlaza el del back.
- **Protección de rama:** documenta en el README la protección recomendada de `main`: 1 aprobación y los jobs del CI como checks obligatorios. Aclara que en GitHub gratuito solo está disponible en repos públicos; para repos privados se necesita GitHub Pro o Team.

## 6. Verificación final (DoD de esta tarea)

**`musubi-api`:**

- `dotnet build Musubi.slnx -c Release` → 0 errores, 0 advertencias.
- `dotnet test` de Unit, Integration y Security con `-c Release --no-build` → verdes; reporta los conteos.
- `pwsh scripts/critic-guardrails.ps1` → PASS y `pwsh scripts/check_endpoints.ps1` → OK.
- YAML del CI validado (`python -c "import yaml; yaml.safe_load(open('.github/workflows/ci-pr.yml'))"`).

**`musubi-app`:**

- EN CASO DE NO TENER INSTALADO FLUTTER, INSTALALO.

- `dart format --output=none --set-exit-if-changed .` → sin cambios.
- `flutter analyze --fatal-infos --fatal-warnings` → 0 observaciones.
- `flutter test` → verde; reporta el conteo.
- `flutter build apk --debug --dart-define-from-file=config/dev.example.json` → compila (confirma el toolchain de Android y el targetSdk 36).
- `pwsh scripts/critic-guardrails.ps1` → PASS.
- YAML del CI validado.

**Los dos:**

- `git status` sin secretos ni artefactos (`bin/`, `obj/`, `build/`, `.dart_tool/`, reportes).
- Cada spec de las fases 00 y 01 sigue la plantilla, y cada criterio cita un comando o test.
- Las secciones compartidas son idénticas en los dos repos.

## 7. Al terminar

1. Actualiza la `Memoria.md` de cada repo: qué se hizo, decisiones con su porqué y errores a evitar.
2. Mueve este archivo a `musubi-api/docs/bitacora/prompt-plantilla-inicial.md` y menciónalo en la `Memoria.md` del front.
3. Entrégame:
   - archivos creados por repo;
   - resultados de la verificación con números;
   - decisiones que debo revisar;
   - pendientes para el equipo: roles PO/SM, umbrales por definir, applicationId, paquetes de Flutter y protección de rama en GitHub;
   - mensajes de commit propuestos, uno por repo:
     - `SCRUM-XX 01-00 Plantilla inicial back SDD + DevSecOps (Unit N/Integration N/Security N, critic PASS, endpoints OK)`
     - `SCRUM-XX 01-00 Plantilla inicial front SDD + DevSecOps (Tests N, analyze 0, critic PASS)`