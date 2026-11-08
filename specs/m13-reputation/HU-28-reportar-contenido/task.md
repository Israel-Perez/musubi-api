# HU-28 — Reportar contenido o usuarios sospechosos (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (08-Nov-2026): motivos de la maqueta, tipos `general`/`user`/`product` y aviso por correo.

## T2 — Datos
- **Crear:** `reports` en `db/008-reportes-bloqueos.js`, `ReportDocument` y los repositorios Mongo e InMemory.
- **Verificar:** `ReputationRepositoryContract` (en memoria; en Mongo en el CI) y `SchemaScriptTests`.
- **Estado:** ✅ Hecho (08-Nov-2026).

## T3 — Servicio y endpoint
- **Crear:** `ReportService`, `POST /api/v1/reports`, rate limit `reports` e `IConversationDirectory`.
- **Verificar:** `ReputationServiceTests`, `ReputationFlowTests`, `ReputationAuthTests` y `ReputationContractTests`.
- **Estado:** ✅ Hecho (08-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (08-Nov-2026): `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `docs/versionado.md`, `01-04` y `Memoria.md`.

## Pendientes
- La app: `musubi-app:HU-28` ("Reportar" en el chat y "Reportar un problema" en Ajustes).
- Crear `/musubi/prod/Reports/NotifyEmail` en Parameter Store con el correo del equipo.
- `NOTE (HU-39)`: bandeja del administrador.
