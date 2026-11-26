# HU-42 — Consultar indicadores de uso (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (25-Nov-2026), con el apartado 19 de la propuesta (Confluence). Decisiones para revisar: usuarios activos por sesión, la conversión aproximada y `aiUsage`.

## T2 — Datos por módulo
- **Qué:** un método por repositorio (`UsageAsync`, `CreatedBetweenAsync`, `StartedBetweenAsync`, `FindSentAsync`, …), `aiUsage` y `db/036`.
- **Prueba:** los contratos de `TestShared/` (en memoria y MongoDB) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho. `DatabaseTest` corre en el CI.

## T3 — Contratos y servicio
- **Qué:** los nueve `I…Indicators`, `UsageIndicatorsService` y `GET /admin/indicators`.
- **Prueba:** `UsageIndicatorsServiceTests`, `IndicatorsFlowTests`, `AdminAuthTests` y `AdminContractTests`.
- **Estado:** ✅ Hecho.

## T4 — Documentación
- **Estado:** ✅ Hecho: endpoints (132), errores, modelo de datos, `db/README.md`, trazabilidad, versionado (alpha.48, esquema 36), README, `AGENTS.md` y `Memoria.md`.

## T5 — Producción
- **Qué:** después de `infra-up` con `0.5.0-alpha.48` y `db/036`, abrir "Indicadores" con la cuenta del equipo y revisar que los números tengan sentido con los datos del piloto.
- **Estado:** ⏳ Por hacer (siguiente despliegue).
