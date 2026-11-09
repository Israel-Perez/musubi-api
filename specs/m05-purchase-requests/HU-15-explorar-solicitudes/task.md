# HU-15 — Explorar solicitudes publicadas (tareas)

## T1 — Detallar la spec
- **Estado:** ✅ Hecho (09-Nov-2026): filtros de la maqueta, lo que ve un visitante, sin las propias y sin índice de palabras.

## T2 — Datos
- **Crear:** `IWantedRepository.FindBoardAsync` (Mongo y en memoria), `IUserRepository.FindManyAsync`, `IMemberDirectory.FindMembersAsync` e `IProfileDirectory.FindCardsAsync`.
- **Verificar:** `WantedRepositoryContract` (`FindBoard_*`) y `AuthRepositoryContract` (`Users_FindMany_*`), en memoria y en MongoDB en el CI.
- **Estado:** ✅ Hecho (09-Nov-2026). Sin script nuevo: usa `ix_community_status_id`.

## T3 — Servicio y endpoint
- **Crear:** `WantedService.BoardAsync`, `GET /api/v1/wanted` y la política de caché `wanted-board`.
- **Verificar:** `WantedServiceTests`, `WantedFlowTests`, `WantedAuthTests` y `WantedContractTests`.
- **Estado:** ✅ Hecho (09-Nov-2026).

## T4 — Documentación
- **Estado:** ✅ Hecho (09-Nov-2026): endpoints, errores, versionado `0.5.0-alpha.9`, trazabilidad y Memoria.

## Pendientes
- La app: `musubi-app:HU-15` (`Se-busca`, `Se-busca-visitante` y "Se busca" de `Inicio`).
- `NOTE (HU-16)` y `NOTE (HU-18)`.
