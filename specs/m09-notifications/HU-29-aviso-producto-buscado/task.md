# HU-29 — Aviso de producto buscado disponible (tareas)

## T1 — Spec
- **Estado:** ✅ Hecho (14-Nov-2026).

## T2 — Cola
- **Prueba:** `IntegrationTest/Matching/WantedMatchFlowTests.cs`.
- **Estado:** ✅ Hecho.

## T3 — Aviso
- **Prueba:** `UnitTest/Matching/ProductMatchTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Base
- **Prueba:** `TestShared/Notifications/NotificationRepositoryContract.cs` y `DatabaseTest/SchemaScriptTests.cs`.
- **Estado:** ✅ Hecho (corre en el CI con MongoDB real).

## T5 — Prueba en el celular
- **Qué:** con la API `0.5.0-alpha.31` desplegada, publicar algo que otra cuenta busca y tocar el aviso.
- **Estado:** ⏳ Por hacer.
