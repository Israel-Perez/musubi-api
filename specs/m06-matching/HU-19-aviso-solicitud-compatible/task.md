# HU-19 — Aviso al vendedor de solicitud compatible (tareas, API)

## T1 — Spec
- **Estado:** ✅ Hecho (13-Nov-2026).

## T2 — Candidatas (M-03)
- **Prueba:** `MatchCandidates_*` (TestShared).
- **Estado:** ✅ Hecho.

## T3 — Reglas y aviso (M-06)
- **Prueba:** `UnitTest/Matching/WantedMatchTests.cs`.
- **Estado:** ✅ Hecho.

## T4 — Segundo plano
- **Prueba:** `WantedServiceTests` (encola) e `IntegrationTest/Matching/WantedMatchFlowTests.cs`.
- **Estado:** ✅ Hecho.

## T5 — Pruebas y documentación
- **Estado:** ✅ Hecho: Unit 638 · Integration 171 · Security 222 · Contract 22.

## T6 — Prueba en el celular
- **Qué:** con la API desplegada, publicar una solicitud desde otra cuenta y ver el aviso en "Coincidencias".
- **Estado:** ⏳ Por hacer.
