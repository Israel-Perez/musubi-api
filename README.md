# musubi-api

API de **MUSUBI**, la app de compra-venta entre estudiantes de la UTTT. Hecha con **ASP.NET Core / .NET 10**, PostgreSQL y EF Core, versionada en `api/v1`.

Se desarrolla con **Spec Driven Development (SDD)** y DevSecOps: cada cambio nace de una spec en `specs/` (plantilla `specs/_template.md`) y se verifica con comandos y pruebas. Las reglas no negociables están en `.specify/memory/constitution.md`.

- Repo hermano (front Flutter): <https://github.com/Israel-Perez/musubi-app>
- Contrato entre repos: [`docs/endpoints.md`](docs/endpoints.md) (canónico; el front lo enlaza y no lo copia)
- Backlog: Jira, proyecto `SCRUM` · Documentación funcional: Confluence

## Estado (15-Oct-2026)

Plantilla inicial lista. Fases 00 (constitución) y 01 (fundación) implementadas en "🚧 Borrador con evidencia", en espera de designar revisores:

- Build Release: 0 errores / 0 advertencias.
- Pruebas: Unit 19/19 · Integration 8/8 · Security 11/11 · Contract 2/2.
- `critic-guardrails` PASS · `check_endpoints` OK (3 rutas) · Semgrep 0 hallazgos · actionlint sin hallazgos.
- Endpoints: `GET /health`, `GET /health/ready`, `GET /api/v1/ping`.

El modelo de datos (`02-01`) y las 47 HU del MVP siguen pendientes (ver [`docs/trazabilidad.md`](docs/trazabilidad.md)).

## Requisitos

- .NET SDK 10.0.4xx (`global.json`)
- PowerShell 7 (`pwsh`) para los guardarraíles (también funcionan en Windows PowerShell 5.1)
- Opcional: Docker (PostgreSQL local), Python 3 + PyYAML, actionlint y Semgrep para validar en local lo mismo que el CI

## Puesta en marcha

```powershell
dotnet restore Musubi.slnx
dotnet run --project Musubi.Api          # Development: InMemory, http://localhost:5080
# http://localhost:5080/api/v1/ping  ·  /health  ·  /health/ready  ·  /scalar (solo Dev)
```

Con PostgreSQL local (detalles en [`deploy/README.md`](deploy/README.md)):

```powershell
Copy-Item deploy/.env.example deploy/.env      # cambia POSTGRES_PASSWORD
docker compose -f deploy/docker-compose.local.yml --env-file deploy/.env up -d
dotnet user-secrets --project Musubi.Api set "UseInMemoryDatabase" "false"
dotnet user-secrets --project Musubi.Api set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=musubi;Username=musubi;Password=<tu contraseña>"
```

**Secretos:** nunca en appsettings versionados (las pruebas y el critic lo impiden). Van en user-secrets, en `appsettings.Local.json` (ignorado, solo en Development) o en las variables/secretos del despliegue. La tabla de claves está en `specs/phase-01-foundation/01-04-config-reference/spec.md`.

## Comandos de calidad (los mismos del CI)

```powershell
dotnet build Musubi.slnx -c Release
dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build
dotnet test --project IntegrationTest/IntegrationTest.csproj -c Release --no-build
dotnet test --project SecurityTest/SecurityTest.csproj -c Release --no-build
dotnet test --project ContractTest/ContractTest.csproj -c Release --no-build
pwsh scripts/critic-guardrails.ps1
pwsh scripts/check_endpoints.ps1
semgrep scan --config=auto --config=.semgrep/semgrep.yaml --error
```

> Las pruebas usan xUnit v3 sobre Microsoft.Testing.Platform (`global.json`), por eso el comando es `dotnet test --project <csproj>`.

## Estructura

```
Musubi.Api/            API: Program.cs, Platform/ (hosting, datos, ping), Modules/ (M-01…M-14, uno por HU)
UnitTest/ IntegrationTest/ SecurityTest/ ContractTest/ DatabaseTest/
specs/                 SDD: _template.md, phase-00-constitution/, phase-01-foundation/, mNN-<modulo>/HU-NN-*/
docs/                  endpoints.md (contrato), trazabilidad.md (47 HU), modulos.md (14 módulos), bitacora/
scripts/               critic-guardrails.ps1, check_endpoints.ps1, check_shared_sections.ps1
deploy/                docker-compose.local.yml (PostgreSQL 18.6)
.claude/               sub-agentes, skills y hooks de Claude Code
.github/               CI, Dependabot, plantilla de PR
.semgrep/              reglas SAST propias
```

Arquitectura: monolito modular en un proyecto (constitución §7). Los 14 módulos y sus carpetas están en [`docs/modulos.md`](docs/modulos.md).

## Flujo SDD

1. Al iniciar el sprint de una HU: crear `specs/mNN-<modulo>/HU-NN-<slug>/{spec,plan,task}.md` (skill `spec-first-writing`).
2. Rama `feature/SCRUM-NN-slug`.
3. Esqueleto con el agente `slice-scaffolder` → implementación y pruebas (skill `vertical-slice`).
4. Gate pre-push: `pwsh scripts/critic-guardrails.ps1` + agente `security-reviewer`; trazabilidad con `traceability-clerk`.
5. PR con la clave Jira en el título (`SCRUM-19 HU-01 Crear cuenta: … (Unit N/Integration N/Security N, critic PASS)`) y el checklist completo. Si la HU toca el front, el PR de `musubi-app` enlaza este.
6. Actualizar `docs/trazabilidad.md` y `Memoria.md`.

Las specs quedan en "🚧 Borrador con evidencia" hasta que el usuario indique un revisor (1 integrante distinto del autor).

## CI

`.github/workflows/ci-pr.yml` corre en cada `pull_request` y `push` a `main`:

| Job | Qué hace |
|---|---|
| `build` | restore + build Release (0/0) |
| `unit`, `integration`, `security`, `contract` | Suites con `--no-build` |
| `critic` | `scripts/critic-guardrails.ps1` (4 bloqueantes) |
| `endpoints` | `scripts/check_endpoints.ps1` (catálogo ↔ código) |
| `semgrep` | SAST (`--config=auto` + reglas propias) |
| `docker-build` | `docker build` del `Dockerfile`, sin push |

Las actions están fijadas por SHA; Dependabot (nuget, github-actions y docker, con `cooldown` de 7 días) las mantiene al día.

## Protección de rama recomendada (`main`)

En GitHub → Settings → Branches (o Rulesets) → regla para `main`:

- Requerir un pull request con **1 aprobación** (de un integrante distinto del autor).
- Requerir que pasen los checks: `build`, `unit`, `integration`, `security`, `contract`, `critic`, `endpoints`, `semgrep` y `docker-build`.
- Requerir que la rama esté actualizada antes del merge y bloquear los force push.

> En GitHub gratuito, la protección de rama (y los rulesets) solo se aplica en **repos públicos**. Para un repo **privado** se necesita GitHub Pro (cuenta personal) o GitHub Team (organización).

## Créditos

Las prácticas de SDD, DevSecOps y guardarraíles se adaptaron del repositorio de referencia **WebAPIDevSecOpsScallingSDD**, compartido en la materia de Aplicaciones Móviles. Se copió la forma de trabajar, no el dominio ni el stack.
