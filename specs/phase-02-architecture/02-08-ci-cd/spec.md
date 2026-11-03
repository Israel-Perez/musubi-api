# 02-08 — CI/CD de musubi-api

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-90 / S4 / — / [musubi-app:02-05](https://github.com/Israel-Perez/musubi-app/tree/main/specs/phase-02-architecture/02-05-ci-cd)

## Contexto
El documento de la materia pide CI/CD para el backend (requisito 8). También pide crear y eliminar la infraestructura como pasos (requisito 15) y versionar cada despliegue (requisito 11).

Hasta ahora las piezas estaban repartidas en tres specs:
- `01-05` creó el CI;
- `02-05` creó `infra-up` e `infra-down`;
- `00-04` fijó las lecciones (actions por SHA, guardas que muerden).

Esta spec las junta en un solo lugar y agrega la protección de `main`.

**Depende de:** `01-05` (CI), `02-05` (infraestructura) y `docs/versionado.md`.

## Requisitos
1. **CI en cada PR y en cada push a `main`** (`ci-pr.yml`). Ningún cambio llega a `main` sin pasar:
   - compilación (0 errores y 0 advertencias);
   - pruebas unitarias, de integración, de seguridad, de contrato y de base de datos (MongoDB real);
   - escaneo de código: critic, Semgrep y el catálogo de endpoints;
   - la imagen Docker.
2. **CD manual** (`workflow_dispatch`), solo desde `main` y en el entorno `produccion`:
   - **`infra-up`:**
     1. guarda de versión;
     2. imagen a ECR;
     3. scripts `db/*.js`;
     4. `terraform apply`;
     5. reemplazo de instancias;
     6. prueba de humo;
     7. etiquetas `api-vX.Y.Z` e `infra-vX.Y.Z`.
   - **`infra-down`:** `terraform destroy`, solo si se escribe `DESTRUIR`.
3. **Sin llaves de larga duración:** GitHub entra a AWS por OIDC (rol `github-deploy`). Los secretos de la API viven en Parameter Store.
4. **Protección de `main`:** el ruleset "Main Proteccion" exige:
   - no borrar la rama;
   - PR con 1 aprobación;
   - todos los checks del CI;
   - sin force push;
   - revisión automática de Copilot.
5. **Dependencias al día:** Dependabot para NuGet, Actions y Docker, con `cooldown` de 7 días. Las actions van fijadas por SHA.

## Diseño
- **`.github/workflows/ci-pr.yml`:**
  - `build` va primero; `unit`, `integration`, `security`, `contract` y `database` dependen de él;
  - `critic`, `endpoints`, `semgrep`, `docker-build` y `terraform` (fmt y validate) corren en paralelo;
  - permisos de solo lectura.
- **`.github/workflows/infra-up.yml`:**
  - la versión sale de `<Version>` de `Musubi.Api.csproj`;
  - si `api-v<versión>` ya existe y la API cambió desde esa etiqueta, falla y pide subirla (lineamiento 11);
  - los scripts de `db/` corren con `mongosh` en un contenedor; son idempotentes;
  - la prueba de humo espera `/health/ready` y `/api/v1/ping` en 200.
- **`.github/workflows/infra-down.yml`:** pide confirmación escrita y deja intacto el estado de Terraform (bucket del bootstrap).
- **Ruleset:** se creó con `gh api repos/{owner}/{repo}/rulesets`. El rol de administrador puede saltar la aprobación **solo desde un PR** (`bypass_mode: pull_request`), mientras se suman revisores al equipo. Nunca puede hacer push directo ni force push.
- **Guía completa:** [`README.md`](../../../README.md) (CI y protección) e [`infra/README.md`](../../../infra/README.md) (secretos, costo y dominio).

## Contratos
- **Checks obligatorios:** `build`, `unit`, `integration`, `security`, `contract`, `database`, `critic`, `endpoints`, `semgrep` y `docker-build`.
- **Entorno `produccion`:** variable `AWS_DEPLOY_ROLE_ARN`, solo para la rama `main`.
- **Etiquetas:** `api-vX.Y.Z[-pre]` e `infra-vX.Y.Z` (`docs/versionado.md`).

## Tests
- `actionlint .github/workflows/*.yml` y `python -c "import yaml; ..."` en local, antes de cambiar un workflow.
- Las guardas se prueban con su autoprueba ("el guard debe morder"):
  - `check_endpoints` con una ruta sin fila;
  - critic con un `TODO`;
  - la guarda de versión con una etiqueta repetida.
- Evidencia de corrida:
  - `infra-up` 41693850956 (03-Nov-2026): API `0.4.0-alpha.1`, reemplazo de instancias "Successful" y prueba de humo en 200;
  - CI del PR #23: 11 checks en verde.

## Criterios
- Un PR a `main` no se puede fusionar con un check en rojo:
  - `gh api repos/Israel-Perez/musubi-api/rulesets` muestra "Main Proteccion" activo;
  - `gh api repos/Israel-Perez/musubi-api/branches/main/protection` lista los 10 checks.
- `infra-up` en `main` deja la API sana y crea las etiquetas.
- `infra-up` falla si la API cambió y no se subió `<Version>`.

## Límites
- **Aprobación del administrador:** mientras no haya un revisor fijo, el administrador puede fusionar su propio PR. El CI sigue siendo obligatorio.
- **Aprobación extra en PR de Copilot sin atribuir:** se activa desde la interfaz de GitHub (Settings → Rules → Rulesets).
- **Fuera de alcance:**
  - SonarCloud, Trivy y ZAP (`02-06`);
  - pruebas de mutación y de rendimiento (SCRUM-94).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 03-Nov-2026
- **Detalle:**
  - junta el CI de `01-05` y el CD de `02-05`, y agrega el ruleset (auditoría de requisitos, SCRUM-90);
  - el ruleset se creó el 03-Nov-2026 en api, app y landing.
