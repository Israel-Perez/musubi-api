# HU-49 — Ver populares y recién publicados (tareas)

## T1 — Confirmar decisiones funcionales
- **Qué:** Confirmar con PO qué cuenta como vista, si cuenta la navegación propia, visitas anónimas/repetidas y cómo se definen Hoy/Semana (zona horaria y límites).
- **Evidencia:** decisión asentada en SCRUM-69 y reflejada en `spec.md`/`plan.md`.
- **Estado:** ✅ Resuelta por el usuario (12-Nov-2026): contar apertura válida, incluyendo visitantes y repeticiones, excluir vendedor; Hoy = día UTC actual y Semana = últimos 7 días UTC.

## T2 — Conciliar contrato API y app
- **Qué:** Acordar campus para visitante/autenticado, orden de empates, estados incluidos, paginación, caché y estados vacíos/error con `musubi-app:HU-49`.
- **Archivos:** `spec.md`, `plan.md` y contraparte `musubi-app/specs/m04-marketplace/HU-49-populares-recientes/`.
- **Verificación:** ambas specs enlazan el mismo contrato; los detalles de cache/periodo quedan explícitos.
- **Estado:** ✅ Acordado en ambas specs: campus de token/visitante, orden estable, periodos, paginación y forma de ranking.

## T3 — Esquema y repositorios
- **Qué:** implementar `productStatsDaily` con incremento atómico `$inc`/upsert, índices y TTL previstos; compartir batería Mongo/InMemory.
- **Archivos:** `Musubi.Api/Modules/Listings/Data/`, `db/022-popularidad.js`, `Platform/Data/MongoSchema.cs`, `db/README.md`, `DatabaseTest/MongoListingRepositoryTests.cs`.
- **Verificación:** contrato de repositorio en memoria y Mongo; `SchemaScriptTests` confirma script, índices, TTL y validador.
- **Estado:** ✅ Script 022/esquema 22 y query Mongo paginada con `$lookup`/`$facet`; Database 145/145 y Unit 615/615.

## T4 — Registro de vistas y ranking
- **Qué:** agregar el registro de vista en detalle y `GET /api/v1/products/popular?period=today|week`; reutilizar `GET /products` para recientes.
- **Archivos previstos:** `ProductsController.cs`, `ListingService.cs`, DTOs, servicio/repositorio y registro DI en `Modules/Listings/`.
- **Verificación:** Unit e Integration cubren periodos, filtros, orden, campus, estados y política confirmada para autor/visitante; `GET /products` conserva cursor y sort.
- **Estado:** ✅ Build 0/0; Integration 173/173 y repositorio InMemory cubiertos con página, orden, total, keyset y estados elegibles.

## T5 — Seguridad, caché y contratos
- **Qué:** declarar auth explícita, evitar contaminación de caché, actualizar catálogo canónico y fixtures.
- **Archivos:** `docs/endpoints.md`, `docs/errores.md` (solo si se define error nuevo), `ContractTest/Fixtures/`, `ContractTest/ListingsContractTests.cs`, `SecurityTest/Auth/`, `SecurityTest/Caching/`.
- **Verificación:** 401/aislamiento por campus según contrato; fixture del ranking; pruebas que demuestran la separación de respuestas cacheadas.
- **Estado:** ✅ Security 223/223, Contract 23/23, Integration 173/173 y Database 145/145, incluida la agregación Mongo, keyset y filtrado de inactivos.

## T6 — Aplicación contraparte
- **Qué:** integrar selector Populares Hoy/Semana en Inicio y navegación de recién publicados con “Más antiguas”.
- **Repositorio:** `musubi-app`, misma carpeta de spec HU-49.
- **Verificación:** análisis, formato y pruebas del repo hermano; estados de carga, vacío y error cubiertos.
- **Estado:** 🟡 Implementación de ranking y recientes presente en el repo hermano; suite completa y pruebas/capturas no verificadas por SDK Dart 3.12.2 frente a ^3.13.5.

## T7 — Verificación y trazabilidad de cierre
- **Qué:** ejecutar suites, guardarraíles y reconciliar documentación/Memoria.
- **Verificación:** `dotnet build Musubi.slnx -c Release` 0/0; Unit, Integration, Security, Contract y Database en verde; `pwsh scripts/check_endpoints.ps1` OK; `pwsh scripts/critic-guardrails.ps1` PASS; reviewer de seguridad y chequeo de trazabilidad antes del PR.
- **Archivos:** `docs/trazabilidad.md`, `Memoria.md` y artefactos de spec.
- **Estado:** 🟡 API verificada; security-reviewer PASS y traceability-clerk OK. Suite completa Flutter pendiente por SDK Dart 3.12.2 frente a ^3.13.5. Commit Jira y PR separados pendientes.
