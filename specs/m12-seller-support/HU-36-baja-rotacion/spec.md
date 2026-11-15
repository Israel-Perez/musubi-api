# HU-36 — Ver mis productos con baja rotación

**HU / Jira / Sprint / Módulo / Contraparte:** HU-36 / SCRUM-59 / Sprint 5 (14 nov – 1 dic 2026) / M-12 / [musubi-app:HU-36](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m12-seller-support/HU-36-baja-rotacion)

## Contexto
Jira (SCRUM-59): Como **vendedor**, quiero **saber qué productos llevan mucho tiempo publicados sin venderse**, para **decidir qué hacer con ellos**.

- Maqueta: `Mis-productos` → panel "Llevan tiempo sin venderse" ("2 productos con más de 3 semanas", "Publicada hace 34 días", "96 vistas", "0 mensajes") y `Notificaciones` ("Tu bata lleva 34 días sin venderse · 3 estudiantes buscan algo similar. Te damos ideas para venderla."). La spec ligera decía que no había pantalla: sí la hay.
- Trazabilidad: RF-50, RF-51 · CU-22 · Flujo C · RN-35, RN-36.
- Decisiones de la usuaria (15-Nov-2026): **una tarjeta por producto** en el panel; las ideas en una **hoja al tocar la tarjeta** ("Ideas para venderla"); el aviso **una sola vez, a las 3 semanas**. El umbral de **21 días** sale de la maqueta ("2 productos con más de 3 semanas").

**Depende de:** `HU-06`/`HU-08` (Mis productos), `HU-49` (vistas), `HU-20` (conversaciones), `HU-23`/`HU-26` (pedidos), `HU-31` (avisos). Se entrega con [HU-37](../HU-37-sugerencias-venta/spec.md) y [HU-38](../HU-38-demanda-productos/spec.md).

## Requisitos
1. **R1 · Cuáles:** las publicaciones **disponibles** de quien consulta con **más de 21 días** publicadas que nunca se vendieron por un pedido completado y que no tienen uno pendiente o confirmado (RN-35, RN-36). Pausadas, agotadas y vendidas no cuentan. De la que lleva más días a la que lleva menos, hasta 20.
2. **R2 · Qué se ve de cada una:** días publicada, vistas de otras personas en los últimos 30 días (los agregados de HU-49 viven 30 días) y conversaciones abiertas desde ella ("mensajes").
3. **R3 · Aviso:** un proceso periódico (cada `Notifications:ReminderMinutes`, 60 por defecto) avisa `slow_listing` **una sola vez** por publicación cuando cumple 21 días (mira dos días hacia atrás por si el servicio estuvo apagado; `dedupeKey` `slow_listing:{productId}`): "Tu bata lleva 21 días sin venderse" (la primera palabra del nombre, o el nombre entre comillas si no se lee bien) y "3 estudiantes buscan algo similar. Te damos ideas para vender más rápido.". Abre `Mis-productos` (`target.type: my_products`).

## Diseño
- Módulo nuevo `Modules/SellerSupport/` (M-12) sin colecciones propias: `SellerSupportService`, `SellerSupportController`, `SlowListingJob` + `SlowListingWorker`.
- Lee por contratos: `ISellerListings` (M-03: disponibles por fecha, vistas, rango de precios), `IProductDemand` (M-06), `IConversationDirectory.CountForProductsAsync` (M-07) e `IOpenOrders` (M-08).
- Repositorios: `IProductRepository.FindAvailableListedAsync`, `IProductStatsRepository.ViewsSinceAsync`, `IConversationRepository.CountByProductsAsync`. Índice `ix_status_created` y validador de avisos en `db/026-apoyo-vendedores.js`.

## Contratos
Fila `GET /api/v1/products/mine/slow` de [`docs/endpoints.md`](../../../docs/endpoints.md); fixture `ContractTest/Fixtures/slow-listings.json`. Sin errores nuevos.

## Tests
- `UnitTest/SellerSupport/SellerSupportServiceTests.cs`: cuáles (umbral, estados, de otra persona), pedidos completados y abiertos, vistas de 30 días y mensajes, el aviso una vez a las 3 semanas con la ventana de dos días, el título y el largo del push.
- `TestShared/Listings/ListingRepositoryContract.cs` → `AvailableListed_…`; `TestShared/Chat/ChatRepositoryContract.cs` → `CountByProducts_…`; `DatabaseTest/MongoListingRepositoryTests.cs` → `ViewsSince_…`; `DatabaseTest/SchemaScriptTests.cs` (`026`).
- `IntegrationTest/SellerSupport/SellerSupportFlowTests.cs`, `SecurityTest/Auth/SellerSupportAuthTests.cs`, `ContractTest/SellerSupportContractTests.cs`.

## Criterios
- [x] Las disponibles con más de 3 semanas sin pedidos completados (RN-35, RN-36) → `Slow_AreTheAvailableOnesWithMoreThanThreeWeeks_TheOldestFirst`, `Slow_LeavesOutWhatSoldByAnOrder_OrHasAnOpenOne`.
- [x] El vendedor las ve con sus vistas y mensajes → `Slow_CountsTheViewsOfThirtyDays_AndTheChatsFromTheListing` e integración.
- [x] Un proceso periódico avisa una vez → `TheNotice_GoesOnceAtThreeWeeks_…`, `TheNotice_LooksTwoDaysBack_…`; el validador lo admite (`EveryTypeAndTargetOfTheCode_CanBeStored`, `DatabaseTest`).
- [x] Build Release 0/0 · Unit 748, Integration 188, Security 244 y Contract 26 · `check_endpoints` OK (97) · critic PASS.

## Límites
- Las vistas son de los últimos 30 días (los agregados diarios de HU-49 tienen TTL de 30 días); una publicación más vieja no muestra su total histórico.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 15-Nov-2026 (detallado e implementado, junto con HU-36, HU-37 y HU-38)
