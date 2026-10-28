<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
# Guía del equipo: cómo trabajar una HU con IA (Claude Code u OpenCode)

Esta guía es para quien va a tomar una historia de usuario (HU) de Jira y entregarla como un PR fusionado. Se trabaja igual con **Claude Code** que con **OpenCode**: las dos herramientas leen las mismas reglas del repo. Si algo de aquí contradice a `.specify/memory/constitution.md` o a `AGENTS.md`, mandan esos archivos; avisa para corregir la guía.

**La idea en una línea:** Jira → rama → la IA escribe la spec → **tú la validas** → la IA implementa con pruebas → verificas → revisión → PR → otra persona aprueba → se fusiona → se despliega → Jira "Finalizado".

---

## 1. Antes de empezar (una sola vez)

### Accesos que necesitas
| Qué | Para qué | A quién se lo pides |
|---|---|---|
| Colaborador en GitHub de **musubi-api** y **musubi-app** | Crear ramas y PR (los dos repos son públicos, pero sin ser colaborador no puedes empujar ramas) | Al dueño de los repos |
| Cuenta en Jira (`uttt-startup-musubi.atlassian.net`, proyecto `SCRUM`) | Tomar HU, moverlas de estado y comentar | Al dueño del proyecto Jira |
| Tu correo institucional verificado en AWS SES | Registrarte en la app que corre en la nube (SES está en modo de prueba y solo envía a correos dados de alta) | Al responsable de la nube; te llega un correo de AWS con un enlace que debes abrir en 24 h |
| La URL de la API en la nube | Probar la app contra AWS (`config/prod.json`) | Pídela en el canal del equipo: cambia cada vez que se recrea la infraestructura |

**No necesitas** credenciales de AWS ni de MongoDB Atlas: el despliegue lo hace el responsable de la nube con el workflow `infra-up`.

### Herramientas
| Repo | Herramientas (versiones medidas) |
|---|---|
| `musubi-api` | .NET SDK 10.0.400 o superior · PowerShell 7 (`pwsh`) · Python 3 + PyYAML · `actionlint` · opcional: `semgrep 1.181.0` y Docker (sin Docker, las pruebas de base de datos corren solo en el CI) |
| `musubi-app` | Flutter **3.47.7** (stable) · JDK 17 · Android SDK (`android-36`, build-tools 36.1.0, NDK 28.2.13676358) · `adb` · PowerShell 7 |

### Clona los dos repos como carpetas hermanas
```powershell
mkdir musubi; cd musubi
git clone https://github.com/Israel-Perez/musubi-api.git
git clone https://github.com/Israel-Perez/musubi-app.git
```
Tienen que quedar así (`musubi/musubi-api` y `musubi/musubi-app`): los scripts y las specs se buscan entre sí por esa ruta.

### Comprueba que todo compila
- **API:** `cd musubi-api` → `dotnet build Musubi.slnx -c Release` (0 errores / 0 advertencias) → `dotnet run --project Musubi.Api` (en Development usa una base en memoria; no necesitas MongoDB).
- **App:** `cd musubi-app` → `Copy-Item config/dev.example.json config/dev.json` → `flutter test` → `flutter run --dart-define-from-file=config/dev.json`.

---

## 2. Cómo "lee" el proyecto tu IA

| Pieza | Qué es | Claude Code | OpenCode |
|---|---|---|---|
| `AGENTS.md` | Reglas operativas del repo (comandos, límites, convenciones) | Lo carga por `CLAUDE.md` (`@AGENTS.md`) | Lo carga directo (es su archivo de reglas) |
| `Memoria.md` | Bitácora: estado actual, decisiones y **errores a evitar** | Pídele que la lea al empezar | Igual |
| `.specify/memory/constitution.md` | Lo que no se negocia (stack, seguridad, calidad) | Igual | Igual |
| `.claude/skills/*/SKILL.md` | Reglas técnicas por tema (spec, pruebas, trazabilidad…) | Las usa sola | También las lee (ruta compatible) |
| Sub-agentes `security-reviewer`, `traceability-clerk`, `slice-scaffolder` | Revisiones y esqueleto de una HU | `.claude/agents/` (con hook de solo lectura) | `.opencode/agents/` (solo lectura por `permission`) |
| `docs/maqueta/musubi-prototipo.html` (en `musubi-app`) | La maqueta: **la app se construye igual a ella** | Igual | Igual |

**Abre la herramienta dentro del repo en el que vas a trabajar** (`musubi-api` para el back, `musubi-app` para el front). Si la abres en la carpeta `musubi/`, no carga las reglas de ningún repo. Para una HU de los dos lados usa una sesión por repo.

---

## 3. El flujo de una HU, paso a paso

### Paso 0 · Toma la HU en Jira
1. Asígnatela y muévela a **En curso**.
2. Avisa en el equipo qué HU tomaste y qué repos toca, para que nadie trabaje lo mismo.
3. Revisa sus dependencias: la tabla `docs/trazabilidad.md` y la sección **Depende de** de specs parecidas. Si depende de algo que no existe, se resuelve con un fake y un `NOTE (HU-NN)` (skill `deferred-scope-fakes`), no se espera.

### Paso 1 · Crea la rama en cada repo que toque
```powershell
git checkout main; git pull
git checkout -b feature/SCRUM-NN-slug-corto      # fix/SCRUM-NN-... si es un error
```
Mismo nombre de rama en `musubi-api` y `musubi-app`.

### Paso 2 · Pide la spec (prompt 1)
La IA debe:
- leer Jira, la maqueta y el código;
- **preguntarte las decisiones que no puede tomar sola** (alcance, datos, reglas de negocio);
- escribir `spec.md`, `plan.md` y `task.md` en `specs/mNN-<modulo>/HU-NN-<slug>/` de cada repo.

Una spec que no te hizo ninguna pregunta suele estar inventando algo.

### Paso 3 · Valida la spec (lo haces tú, no la IA)
Antes de pedir código, revisa:
- [ ] Cada criterio de aceptación de Jira está en **Requisitos** y tiene su prueba en **Tests**.
- [ ] Cita las **pantallas de la maqueta** por su ID y no quita nada de lo que aparece en ellas (lineamiento 4: se puede agregar, nunca quitar).
- [ ] Lo que no entra está en **Límites**, con la HU dueña (`NOTE (HU-NN)`).
- [ ] Endpoints nuevos: método, ruta, auth y códigos de error. Colecciones nuevas: campos e índices.
- [ ] Los **Criterios** se verifican con un comando o una prueba ("funciona bien" no es un criterio).
- [ ] Si la HU necesita un paquete nuevo, la spec aplica la regla de excepción de `00-01` y **el equipo lo aprueba**.

Si algo no te convence, corrígelo con la IA antes de seguir. Cambiar la spec es barato; cambiar el código, no.

### Paso 4 · Implementa (prompts 2 y 3)
- **Primero el back** (`musubi-api`):
  - script `db/NNN-*.js` si cambia la base, datos, servicio y controller;
  - pruebas: Unit, Integration, Security y Contract;
  - filas en `docs/endpoints.md` y `docs/errores.md`.
- **Después el front** (`musubi-app`):
  - modelo, repositorio, estado, pantalla y ruta;
  - widget tests de cada estado (carga, vacío, error y éxito);
  - **capturas lado a lado con la maqueta** (`flutter test tool/screenshots_test.dart --dart-define=SHOTS=<carpeta>`).
- Si el back aún no existe, el front avanza con un repositorio fake y `NOTE (musubi-api:HU-NN)`.

### Paso 5 · Verifica en tu máquina (lo mismo que corre el CI)
| `musubi-api` | `musubi-app` |
|---|---|
| `dotnet build Musubi.slnx -c Release` → 0/0 | `dart format --output=none --set-exit-if-changed .` |
| `dotnet test --project UnitTest/UnitTest.csproj -c Release --no-build` (igual para `IntegrationTest`, `SecurityTest` y `ContractTest`) | `flutter analyze --fatal-infos --fatal-warnings` |
| `pwsh scripts/critic-guardrails.ps1` → PASS | `flutter test` |
| `pwsh scripts/check_endpoints.ps1` → OK | `pwsh scripts/critic-guardrails.ps1` → PASS |
| `pwsh scripts/check_shared_sections.ps1` → OK | `flutter build apk --debug --dart-define-from-file=config/dev.example.json` |

Pide los números reales; no aceptes un "ya pasan las pruebas" sin la salida del comando.

### Paso 6 · Revisión con los sub-agentes
- `security-reviewer`: revisa tu diff contra OWASP, sin editar nada.
- `traceability-clerk`: revisa que spec, código, pruebas, Jira y la contraparte coincidan.

Corrige lo que marquen como **FAIL** antes del PR.

### Paso 7 · Cierra la documentación
- `spec.md`, `plan.md` y `task.md` conciliados con lo que de verdad quedó: nombres de pruebas, conteos y decisiones.
- Estado de la spec: **🚧 Borrador con evidencia**. Pasa a **✅ Aprobado** solo cuando otra persona la revisa.
- La fila de tu HU en `docs/trazabilidad.md` de cada repo.
- Una entrada nueva en `Memoria.md`: qué se hizo, decisiones con su porqué y errores a evitar.

### Paso 8 · Abre el PR
- **Título:** `SCRUM-NN HU-NN Título: resumen (conteos de pruebas, critic PASS)`. La plantilla del PR trae el checklist y es obligatoria.
- **Contraparte:** primero el PR del back y después el de la app; cada uno enlaza al otro.
- **Jira:** mueve la HU a **En revisión** y comenta los enlaces de los PR.
- **Aprobación:** `main` exige **1 aprobación de otra persona** y todos los checks del CI en verde.
- **Orden de fusión:** primero el back, después la app. Si tu rama sale de otra rama sin fusionar (PR apilado), fusiona en orden y rebasa la hija (sección 5).

### Paso 9 · Despliegue y cierre
1. El responsable de la nube sube la versión, corre `infra-up` y aplica el script de la base.
2. Prueba en tu celular contra AWS (sección 6).
3. Si todo funciona, mueve la HU a **Finalizado**.

---

## 4. Prompts listos para copiar

Cambia `HU-NN`, `SCRUM-NN` y las pantallas. Sirven igual en Claude Code y en OpenCode.

**Prompt 1 — Spec** (en `musubi-api`; si la HU es solo de front, en `musubi-app`)
```text
Vamos a trabajar la HU-NN (Jira SCRUM-NN) con el flujo SDD del proyecto.
1. Lee Memoria.md, AGENTS.md y la constitución de este repo y de ../musubi-app.
2. Busca SCRUM-NN en Jira (si no tienes acceso, aquí va el texto: <pega la HU y sus criterios>).
3. Concilia la HU con la maqueta (../musubi-app/docs/maqueta/musubi-prototipo.html, pantallas <IDs>):
   qué pide Jira, qué muestra la maqueta y qué falta o choca.
4. Antes de escribir, pregúntame las decisiones de alcance o de negocio que no puedas resolver.
5. Escribe spec.md, plan.md y task.md en specs/mNN-<modulo>/HU-NN-<slug>/ de los dos repos
   (plantilla specs/_template.md), actualiza docs/trazabilidad.md a "Spec en borrador",
   corre critic y haz commit en la rama feature/SCRUM-NN-<slug> sin hacer push.
```

**Prompt 2 — Back**
```text
Implementa la spec de HU-NN en musubi-api siguiendo su task.md (skill vertical-slice):
datos y script de la base si aplica, servicio, controller, errores del catálogo y las pruebas que lista la spec.
Agrega las filas a docs/endpoints.md y docs/errores.md. Al terminar corre build Release, Unit, Integration,
Security, Contract, critic, check_endpoints y check_shared_sections, y dame los números reales.
```

**Prompt 3 — Front**
```text
Implementa la spec de HU-NN en musubi-app siguiendo su task.md, igual a la maqueta (pantallas <IDs>):
usa las piezas de lib/core/widgets y los tokens de 02-04, consume las filas de ../musubi-api/docs/endpoints.md
desde api_paths.dart y prueba cada estado. Agrega las escenas a tool/screenshots_test.dart y compara las capturas
con la maqueta en claro y oscuro. Al terminar corre format, analyze, test, critic y el APK de debug.
```

**Prompt 4 — Revisión y PR**
```text
Corre los agentes security-reviewer y traceability-clerk sobre mi diff y corrige los FAIL.
Concilia spec/plan/task con lo implementado, actualiza docs/trazabilidad.md y Memoria.md,
haz commit y prepárame el título y la descripción del PR con la plantilla del repo (no hagas push sin preguntarme).
```

**Prompt para retomar una sesión**
```text
Retomamos la HU-NN. Lee Memoria.md, la spec y el task.md de specs/mNN-*/HU-NN-*/ y el git log de la rama;
dime qué tareas faltan y continúa con la siguiente.
```

---

## 5. Lo que choca cuando trabajamos varias personas a la vez

Estos archivos y números los tocan casi todas las HU. Quien fusiona **segundo** actualiza su rama (`git fetch` y `git rebase origin/main`) y corrige lo suyo.

| Recurso | Qué pasa | Cómo se resuelve |
|---|---|---|
| `db/NNN-*.js` y `MongoSchema.ExpectedVersion` (api) | Dos HU pueden crear el mismo número de script | Antes de crear un script, avisa el número en el equipo. Si choca, renumera el tuyo y su `ExpectedVersion` |
| `<Version>` del API, `version` de `pubspec.yaml` y `docs/versionado.md` | Dos HU suben a la misma versión | La versión se decide al fusionar; las pruebas `VersioningTests` / `versioning_test.dart` revisan que coincidan |
| `Memoria.md` ("Estado actual" y números `T`) | Conflicto casi seguro | Conserva las dos entradas y renumera la tuya |
| `docs/trazabilidad.md` | Cada quien cambia su fila | Toca **solo** la fila de tu HU |
| `docs/endpoints.md` ("Total: N filas") y conteos en `AGENTS.md` | Los dos suman filas | Recalcula el total después del rebase |
| `lib/core/network/api_paths.dart`, `lib/app/router.dart`, `musubi_icons.dart` (app) | Varias HU agregan líneas | Al rebasar, deja las líneas de las dos HU |
| Secciones compartidas (`<!-- Sección compartida … -->`) | Deben ser idénticas en los dos repos | Si cambias una, cámbiala en el otro repo y corre `check_shared_sections` |

**PR apilado** (tu rama sale de otra rama sin fusionar): fusiona primero el PR padre. Después rebasa el tuyo con `git rebase --onto origin/main <último commit del padre>` y `git push --force-with-lease`. Si se fusiona fuera de orden, `main` se rompe.

---

## 6. Probar en el celular

| Contra qué | Cómo |
|---|---|
| **AWS** (la API desplegada) | Crea `config/prod.json` con la URL de la API (pídela en el equipo; `config/*.json` está ignorado por Git) y compila con `flutter build apk --profile --dart-define-from-file=config/prod.json`. Para registrarte, tu correo debe estar dado de alta en SES (sección 1) |
| **Tu API local** con el celular por USB | En `musubi-api`, `dotnet run --project Musubi.Api`. Después `adb reverse tcp:5080 tcp:5080` y `flutter run --dart-define-from-file=config/usb.json` (copia de `usb.example.json`). Si el cable se mueve, repite el `adb reverse` |

**Si la app dice "sin conexión"**, casi siempre compilaste con `dev.example.json` (que solo sirve en el emulador) o con la URL de ejemplo. No es tu internet.

**Si no puedes instalar sobre otra versión**, desinstala la anterior. Cada PC firma la app de depuración con una llave distinta.

---

## 7. Reglas que el CI hace cumplir (y que te van a rechazar si no las sigues)

- **Nada de secretos.** No van llaves, contraseñas ni tokens en el código, los `appsettings`, `config/*.json`, `--dart-define` o el CI. Todo lo que va en el APK es público.
- **Nada de `TODO`.** Lo pendiente se escribe `NOTE (HU-NN)` y se anota en el `task.md` de la spec dueña. En los comentarios de C# tampoco escribas la palabra "todo": el analizador la confunde con un `TODO`.
- **Auth explícita** en cada controller (`[Authorize]` o `[AllowAnonymous]`). No pongas `try/catch` en los controllers: los errores salen con su código del catálogo.
- **Cada ruta tiene su fila** en `docs/endpoints.md` y cada fila tiene su ruta (`check_endpoints`).
- **En la app:**
  - nada de `http://` en `lib/`;
  - nada de `print`;
  - ningún `catch` vacío;
  - nada de Dio desde la pantalla: pantalla → provider → repositorio.
- **Estilo:** código en inglés; textos de la app, specs y documentos en español.
- **Paquetes nuevos** solo con la regla de excepción de `00-01` y con el visto bueno del equipo.
- **No copies tablas vivas** (endpoints, errores, modelo de datos) en las specs: enlaza la fila.

---

## 8. Qué no hacer

- **No hagas push a `main`.** Está protegida; todo entra por PR.
- **No fusiones tu propio PR** sin la aprobación de otra persona.
- **No corras `infra-up` ni `infra-down`**, y no toques AWS ni Atlas: cuestan dinero y cambian la URL de todos.
- **No marques una spec como "✅ Aprobado"** sin un revisor distinto del autor.
- **No subas** `config/*.json` reales, `appsettings.Local.json`, `key.properties`, archivos `.jks` ni los archivos de Firebase.
- **No pongas nombres reales de personas** en el código, las pruebas ni los datos de ejemplo.
- **No le creas a la IA sin evidencia:** pide siempre la salida de los comandos y revisa las capturas tú mismo.
<!-- Fin de sección compartida -->
