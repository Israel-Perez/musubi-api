# 00-01 — Principios y stack

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) · contraparte conceptual: `musubi-app:00-01`

## Contexto
Primera spec de la constitución de musubi-api: registra los principios no negociables de MUSUBI con su consecuencia práctica en el back, el stack obligatorio y la regla de excepción. **Depende de**: —

## Requisitos
1. Listar los principios con su consecuencia práctica en el back (fuente: constitución §2, sección compartida).
2. Tabla del stack por capa (constitución §6).
3. Definir la regla de excepción para introducir tecnologías fuera del stack.
4. Ser citada desde `AGENTS.md` y `00-02`.

## Diseño

| Principio (constitución §2) | Consecuencia práctica en musubi-api |
|---|---|
| Seguridad primero | Auth explícita por controller (critic B3 + `SecurityTest/Auth/ExplicitAuthTests.cs`), HSTS fuera de Dev, CORS restringido, errores genéricos fuera de Dev, sin secretos en appsettings versionados. |
| Privacidad por diseño | DTOs de respuesta sin datos sensibles (critic B2). Logs sin PII. Eliminación de cuenta real en HU-46. |
| IA y ML desacoplados | `Modules/GenAi` y `Modules/Recommendations` exponen una interfaz con un fake; su falla no rompe búsqueda, chat ni pedidos (RNF-15). |
| Multi-comunidad | La universidad es un dato del modelo (`02-01`), no una constante. |
| Trabajo pesado en segundo plano | Coincidencias, notificaciones e IA fuera del request (BackgroundService o cola), con el diseño en la spec de cada HU. |
| Empírico antes que teórico | Versiones y umbrales medidos (Memoria.md registra las versiones reales); umbrales sin medir quedan como 🎯 en `00-03`. |
| Documentar límites | `NOTE (ID)` en 4 capas; nunca `TODO` (S1135 + critic B1 + Semgrep). |

Stack: ver la constitución §6 (tabla canónica; no se duplica aquí).

**Regla de excepción** (tecnología, librería o servicio fuera del stack):
1. Proponerla en el `plan.md` de la spec que la necesita, con su licencia, su mantenimiento (último release y actividad) y sus avisos de seguridad (NuGetAudit, GitHub Advisories).
2. Documentar la alternativa descartada, el riesgo y el plan de remoción.
3. Actualizar la constitución §6 y el `Detalle` del bloque de aprobación, y registrarlo en `Memoria.md`.
4. Si la nueva dependencia agrega paquetes, actualizar el Package Source Mapping de `nuget.config` y verificar con caché vacía (`00-04`, regla 4).

## Contratos
N/A — spec constitucional. Referencia normativa en `AGENTS.md` §1 y en la constitución §6.

## Tests
N/A — verificación estructural (plantilla + bloque de aprobación) y por los guardarraíles que hacen cumplir cada principio (columna "consecuencia práctica").

## Criterios
- Cada principio cita una consecuencia práctica verificable (tabla de Diseño).
- El stack cubre todas las capas (constitución §6) y `dotnet build Musubi.slnx -c Release` compila solo con paquetes del stack (`dotnet list Musubi.slnx package`).
- `AGENTS.md` y `00-02` citan `00-01` (`grep -n "00-01" AGENTS.md specs/phase-00-constitution/00-02-sdd-governance/spec.md`).

## Límites
No fija versiones exactas de paquetes (viven en los `.csproj` y en Memoria.md) ni umbrales (viven en `00-03`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 20-Oct-2026
- **Detalle:** Adaptada de la referencia WebAPIDevSecOpsScallingSDD (00-01) al stack y principios de MUSUBI. 20-Oct-2026 (SCRUM-66): datos PostgreSQL/EF Core → MongoDB por el lineamiento 14 (NoSQL); se agregan caché (Redis/Valkey) y moderación (Rekognition/Comprehend) a la constitución §6; los lineamientos de la materia mandan sobre la constitución (§1).
