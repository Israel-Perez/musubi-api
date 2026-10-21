# AGENTS.md — Guía operativa de musubi-api para agentes y desarrolladores

> Qué no se negocia: `.specify/memory/constitution.md` (manda ante cualquier conflicto).
> Principios y stack: `specs/phase-00-constitution/00-01-principles-stack/spec.md`. No se introducen tecnologías fuera del stack sin la regla de excepción de 00-01.
> Repo hermano: [`musubi-app`](https://github.com/Israel-Perez/musubi-app) (front Flutter). El contrato entre ambos es `docs/endpoints.md`.

## 1. Forma de trabajo

- **Setup:** `dotnet restore Musubi.slnx` → `dotnet run --project Musubi.Api` (Development usa `UseInMemoryDatabase=true`, sin MongoDB; API en `http://localhost:5080`, Scalar en `/scalar`). Para usar MongoDB local o Atlas, ver `deploy/README.md`.
- **Comandos:**
  - `dotnet build Musubi.slnx -c Release` (0 errores / 0 advertencias)
  - `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` (igual para `IntegrationTest`, `SecurityTest` y `ContractTest`)
  - `pwsh scripts/critic-guardrails.ps1` · `pwsh scripts/check_endpoints.ps1`
- **Orden del CI (`ci-pr.yml`):** `build` → `unit` / `integration` / `security` / `contract`; en paralelo `critic`, `endpoints`, `semgrep` y `docker-build`.
- **Flujo SDD por HU:** spec (`specs/mNN-<modulo>/HU-NN-<slug>/`, skill `spec-first-writing`) → `slice-scaffolder` (Fase A) → implementación y pruebas (skill `vertical-slice`) → `security-reviewer` → `traceability-clerk` → PR.
- **Ramas y PR:** `feature/SCRUM-NN-slug` / `fix/SCRUM-NN-slug`; un PR = una HU o un fix; título con la clave Jira; checklist de `.github/pull_request_template.md` obligatorio.
- **Naming:** código en inglés, JSON camelCase, documentos y specs en español.
- Sin secretos, sin `TODO` (solo `NOTE (ID)`), sin usings sin usar, sin código muerto.
- Al terminar, resume qué cambiaste y qué decisiones debe revisar el usuario.

## 2. Memoria

> **Gobernanza SDD:** plantilla obligatoria en `specs/_template.md` y DoD en `specs/phase-00-constitution/00-02-sdd-governance/spec.md`. Cada spec nueva sigue la plantilla; los criterios siempre se verifican con un comando o una prueba.

- `AGENTS.md`: manual operativo vivo. `Memoria.md`: bitácora del estado, las decisiones y las lecciones.
- **Al empezar**, lee `Memoria.md` para conocer el estado del proyecto y las decisiones tomadas.
- **Al terminar una tarea**, actualízala: estado actual, decisiones importantes (con su porqué) y errores a evitar.
- **Al cerrar una spec**, concilia `spec.md`/`plan.md`/`task.md` con lo implementado (rutas de pruebas, conteos y nombres reales) y resuelve el bloque de aprobación: "Borrador con evidencia", o "Aprobado" solo si el usuario indicó un revisor.
- Mantenla breve (máximo ~10 000 líneas): resume o elimina lo que ya no aporte.
- Si algo se vuelve regla permanente, propón moverlo a `AGENTS.md` o a `00-04` en lugar de dejarlo en la memoria.
- Nunca guardes datos sensibles (llaves, tokens, datos personales).
- Si cambias una **sección compartida** (marcada con `<!-- Sección compartida … -->`), cámbiala también en `musubi-app`, regístralo en las dos `Memoria.md` y corre `pwsh scripts/check_shared_sections.ps1`.

## 3. Límites

- No uses la palabra española "todo" en comentarios C#: S1135 rompe el build.
- No leas configuración de forma eager en el registro DI si las pruebas la sobrescriben: usa la sobrecarga con `IServiceProvider` (ver `AddMusubiMongo` en `Platform/Data`).
- No reordenes el pipeline de `UseMusubiPipeline` sin actualizar la spec `01-02`.
- No pongas `try/catch` en controllers (critic B4).
- No hardcodees connection strings, llaves ni credenciales; tampoco en appsettings versionados.
- No dupliques tablas vivas en las specs: el canónico vive en `docs/`, la spec enlaza.
- No corras pruebas `--no-build` sobre un build que falló: leerías resultados de binarios viejos.
- En Git Bash usa `-p:` y no `/p:` en los comandos de `dotnet`.
- Siempre: actualiza `Memoria.md` al terminar cada tarea.

## 4. Verificación

- Build Release 0/0 antes de cualquier prueba.
- Unit, Integration, Security y Contract con `--no-build`: 100 % en verde en local.
- `pwsh scripts/critic-guardrails.ps1` → `PASS` exit 0 (también con `powershell -File` en 5.1).
- `pwsh scripts/check_endpoints.ps1` → `OK` exit 0 (cada ruta con fila y cada fila con ruta).
- YAML del CI validado en local: `python -c "import yaml; yaml.safe_load(open('.github/workflows/ci-pr.yml'))"` y `actionlint .github/workflows/ci-pr.yml`.
- SAST en local antes del primer push de una rama: `semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error`.

### 4.2 Verificación de requerimientos no funcionales

- Tabla completa y comandos: `specs/phase-00-constitution/00-03-nfr/spec.md`.
- Guardarraíl: si un cambio impacta un umbral (latencia, tamaño de cuerpo, disponibilidad), ejecuta su comando de verificación antes de pedir la aprobación.

### 4.3 Referencia de configuración

La tabla canónica de claves y su estado por fase vive en `specs/phase-01-foundation/01-04-config-reference/spec.md`. `appsettings.Example.json` solo documenta claves con código real. Los secretos van en user-secrets (`dotnet user-secrets --project Musubi.Api set …`) o en `appsettings.Local.json` (ignorado).

## 5. Convenciones técnicas

> Fase 01 implementada (15-Oct-2026), en "Borrador con evidencia". Fuente viva de rutas: `docs/endpoints.md` (3 filas; la verifica `scripts/check_endpoints.ps1`).

- Rutas `api/v{version:apiVersion}/<recurso>` con `[ApiVersion("1.0")]`; módulos en `Musubi.Api/Modules/<Modulo>/` (ver su README).
- Health: `/health` (liveness) y `/health/ready` (readiness con la BD). OpenAPI (`/openapi/v1.json`) y Scalar (`/scalar`) solo en Development.
- Datos: MongoDB (`IMongoClient`/`IMongoDatabase` lazy en `Platform/Data/MongoServiceCollectionExtensions.cs`; sin MongoDB con `UseInMemoryDatabase=true`); el modelo de documentos nace en `02-01`.
- Versión: `<Version>` de `Musubi.Api.csproj`, alineada con `docs/versionado.md` (lo verifica `UnitTest/Common/VersioningTests.cs`).
- Lineamientos de la materia (criterios de evaluación): `docs/bitacora/lineamientos-materia.md`.
- Pendientes con dueño: autenticación JWT → HU-02; SignalR → HU-20; FCM → HU-31; IA → HU-32; ML → HU-35; Mercado Pago → HU-43.

## 6. Seguridad y DevSecOps

- HSTS de 365 días solo fuera de Dev; HTTPS obligatorio; CORS restringido por configuración (S5122 lo impone en el build).
- Errores fuera de Dev: `ProblemDetails` genérico sin detalle interno.
- Auth explícita por controller (critic B3 + `SecurityTest/Auth/ExplicitAuthTests.cs`). Referencia: OWASP API Security Top 10 – 2023 (checks del agente `security-reviewer`).
- SAST: Semgrep (`--config=auto` + 5 reglas propias). Dependencias: NuGetAudit (incluidas las transitivas) + Dependabot con `cooldown` de 7 días.
- Contenedor: multi-stage con usuario no root; Trivy y ZAP llegan en `02-05`; SonarCloud en `02-06`.
- Las llaves privadas (IA, Mercado Pago, Firebase Admin) viven solo en el back, nunca en el front.

## 7. Testing

- xUnit v3 sobre Microsoft.Testing.Platform (`global.json` → `test.runner`): el comando es `dotnet test --project <csproj>`.
- WebApplicationFactory con `UseInMemoryDatabase=true` (sin MongoDB) en Integration, Security y Contract; las suites WAF corren en serie (`xunit.runner.json`).
- SecurityTest usa factories por ambiente (`ProductionApiFactory`, `DevelopmentApiFactory`) y un controller de prueba (`ThrowingProbeController`) que solo existe en ese ensamblado.
- Matriz completa: skill `testing-strategy`.

## 8. Observabilidad

> 🚧 Pendiente (`02-06`): logs estructurados, correlación por request, métricas y monitoreo del piloto.

## 9. Despliegue y operación

> 🚧 Pendiente (`02-05`): AWS con Terraform (ALB + 2 EC2 en Auto Scaling Group + CloudFront; imágenes en S3), MongoDB Atlas M0, ForwardedHeaders con el proxy real, `AllowedHosts`, respaldos (RNF-16) y los workflows para crear y eliminar la infraestructura.

## 10. Reglas basadas en lecciones aprendidas

La fuente canónica es `specs/phase-00-constitution/00-04-lessons-learned/spec.md` (13 heredadas + 9 propias). Ningún agente debe contradecirlas.

## 11. Sub-agentes (híbrido agentes → skills)

- `slice-scaffolder`: Fase A del lado back de una HU (esqueleto compilable con pruebas que asertan cada diferido); nunca se commitea sin su Fase B.
- `security-reviewer`: gate pre-push contra OWASP API Top 10 + constitución; **solo lectura forzada** (`disallowedTools` + hook `readonly-git-guard.ps1`).
- `traceability-clerk`: spec ↔ código ↔ test ↔ Jira; solo lectura forzada.
- Invocación manual en desarrollo; en el CI solo corren los scripts (`critic`, `endpoints`).
- Fuente de reglas: `.claude/skills/` (10 skills); los agentes las enlazan, nunca las copian. Convención: `.claude/agents/README.md`.

---

## Estructura de la solución

`Musubi.slnx` usa el formato XML `.slnx` (no `.sln`).

| Proyecto / carpeta | Ruta | Tipo |
|---|---|---|
| `Musubi.Api` | `Musubi.Api/` | Web API (`Program.cs`, `Platform/`, `Modules/`) |
| `UnitTest` | `UnitTest/` | xUnit v3 |
| `IntegrationTest` | `IntegrationTest/` | xUnit v3 + WebApplicationFactory |
| `SecurityTest` | `SecurityTest/` | xUnit v3 + WebApplicationFactory por ambiente |
| `ContractTest` | `ContractTest/` | Forma JSON contra `Fixtures/` |
| `DatabaseTest` | `DatabaseTest/` | Esqueleto (Testcontainers MongoDB en `02-01`) |
| `docs/` | `docs/` | `endpoints.md` (3 filas), `trazabilidad.md` (47 HU), `modulos.md` (14 módulos), `versionado.md`, `bitacora/` (lineamientos) |
| `scripts/` | `scripts/` | critic, endpoints, secciones compartidas |
| `deploy/` | `deploy/` | Compose de MongoDB local |
| `.claude/` | `.claude/` | `agents/` (3), `skills/` (10), `hooks/` |
| `.github/` | `.github/` | CI, Dependabot, plantilla de PR |
| `.semgrep/` | `.semgrep/` | Reglas SAST propias |
