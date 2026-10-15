# 00-04 — Lecciones aprendidas (musubi-api)

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte: `musubi-app:00-04`

## Contexto
Reglas operativas derivadas de fallos reales: las de la referencia que aplican a MUSUBI y las medidas al crear esta plantilla. Es la fuente canónica; `AGENTS.md` §10 solo la enlaza. **Depende de**: `00-01`.

## Requisitos
1. Cada regla cita su evidencia (proyecto/fase/fecha) y la spec o skill donde aplica.
2. Sin duplicados con `AGENTS.md` (allí solo hay un enlace).
3. Se agregan reglas cuando un error real se repite o cuesta más de 30 minutos.

## Diseño

### Heredadas de la referencia

| # | Regla | Evidencia | Aplica en |
|---|---|---|---|
| 1 | Verificar empíricamente antes de escribir (versiones, rutas, nombres, formatos). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | skill `empirical-verification` |
| 2 | Medir los runtimes reales antes de fijar timeouts o umbrales. | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `00-03` |
| 3 | Leer la configuración de forma lazy en el registro DI cuando las pruebas la sobrescriben (`WebApplicationFactory`). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `Platform/Hosting/PlatformServiceCollectionExtensions.cs` (`AddDbContext((provider, options) => …)`) |
| 4 | Revisar el estado de publicación de los paquetes y verificar el Package Source Mapping con caché vacía (una caché caliente oculta un NU1100). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `nuget.config`, `00-01` |
| 5 | Documentar los límites conocidos en vez de hacer hacks (`NOTE (ID)` en 4 capas). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | skill `traceability` |
| 6 | Pruebas de frontera exactas (`>=`/`<`, mensajes y ambas ramas). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | skill `testing-strategy` |
| 7 | Herramientas con socket real → proceso real, puerto libre y limpieza en `finally`. | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `DatabaseTest` (Testcontainers, `02-01`) |
| 8 | Scripts con respaldo explícito y salida determinista (`exit 0/1`). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `scripts/` (el critic analiza el repo completo si no existe `origin/main`) |
| 9 | `.gitignore` defensivo para artefactos y secretos locales. | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `01-05` |
| 10 | Diagnosticar con un grupo de control antes de tocar código (`status 0` no implica OK). | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `SecurityTest/Errors/ExceptionHandlingTests.cs` (Development como control) |
| 11 | Actions fijadas por SHA y majors de actions relacionadas actualizados en pareja. | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | `.github/workflows/ci-pr.yml`, Dependabot |
| 12 | Validar el YAML del CI en local antes del push. | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | PyYAML + actionlint |
| 13 | `NOTE (ID)` y nunca `TODO`: S1135 + `TreatWarningsAsErrors` lo convierten en error de build. | Referencia WebAPIDevSecOpsScallingSDD, oct 2026 | critic B1, Semgrep `musubi-todo-marker` |

### Propias de MUSUBI

| # | Regla | Evidencia | Aplica en |
|---|---|---|---|
| 14 | Secretos solo en user-secrets, en archivos ignorados (`appsettings.Local.json`, `deploy/.env`) o en secretos del despliegue; **nunca** en appsettings versionados. | Plantilla inicial MUSUBI, 15-Oct-2026 | `UnitTest/Common/AppSettingsTests.cs`, critic B2, Semgrep |
| 15 | En comentarios de C# no se usa la palabra española "todo": S1135 la trata como marcador TODO y rompe el build. Usar "cada", "toda" o "completo". | Medido en `SecurityTest/Auth/ExplicitAuthTests.cs`, 15-Oct-2026 | Código C# |
| 16 | xUnit v3 (4.x) usa Microsoft.Testing.Platform: en .NET 10 requiere `"test": { "runner": "Microsoft.Testing.Platform" }` en `global.json` y el comando `dotnet test --project <csproj>`. | Medido (error "Testing with VSTest target is no longer supported"), 15-Oct-2026 | `global.json`, CI |
| 17 | En Git Bash, `/p:Prop=...` se convierte en una ruta: usar `-p:Prop=...`. | Medido al restaurar con caché vacía (MSB1008), 15-Oct-2026 | Scripts y documentación |
| 18 | Un guardarraíl solo vale si muerde: probarlo rompiendo el código a propósito (self-test) antes de darlo por bueno. | Medido: sin `UseHsts` → 1 prueba en rojo; scratch → critic FAIL 5/5; ruta faltante y fila huérfana → endpoints FAIL, 15-Oct-2026 | skill `critic-guardrails`, skill `drift-guards` |
| 19 | HSTS **sí** se verifica en el wire con WebApplicationFactory si el host no es localhost (`https://api.musubi.test`): el middleware excluye localhost. Corrige la lección de la referencia. | Medido en `SecurityTest/Hsts/HstsTests.cs`, 15-Oct-2026 | skill `testing-strategy` |
| 20 | SonarAnalyzer S5122 impide compilar `AllowAnyOrigin()`: CORS abierto se detecta en el build y no solo en la revisión. | Medido, 15-Oct-2026 | `01-02` |
| 21 | Ejecutar `semgrep --config=auto` en local antes del primer PR: detectó la falta de `cooldown` en Dependabot. | Medido, 15-Oct-2026 | `.github/dependabot.yml` |
| 22 | Si una versión mutada no compila, las pruebas corren sobre los binarios anteriores y el resultado es falso: revisar `0 Errores` antes de leer el resultado. | Medido al hacer self-test de CORS, 15-Oct-2026 | skill `empirical-verification` |

## Contratos
N/A — spec constitucional. Referencia normativa desde `AGENTS.md` §10.

## Tests
N/A — verificación manual: 22 filas, sin celdas vacías y sin reglas duplicadas.

## Criterios
- 22 reglas, cada una con evidencia y cross-link no vacíos.
- `AGENTS.md` §10 y `Memoria.md` enlazan esta spec (`grep -n "00-04" AGENTS.md Memoria.md`).

## Límites
Las reglas heredadas se citan como evidencia externa (referencia, oct 2026); se vuelven evidencia propia cuando MUSUBI las ejercite (p. ej. la regla 7 con Testcontainers en `02-01`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 15-Oct-2026
- **Detalle:** 13 reglas heredadas (adaptadas) + 9 propias medidas al crear la plantilla.
