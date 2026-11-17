# HU-44 — Coordinar la entrega en un punto del campus

**HU / Jira / Sprint / Módulo / Contraparte:** HU-44 / SCRUM-56 / Sprint 4 (9 – 20 nov 2026) / M-08 / [musubi-app:HU-44](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-44-coordinar-entrega)

## Contexto
Jira (SCRUM-56): Como **comprador o vendedor**, quiero **acordar un punto de encuentro dentro del campus para la entrega**, para **concretar la operación de forma segura sin salir de la aplicación**.

- Maqueta: `Pedido-pago` ("Entrega": punto, día y hora), `Pedido-entrega`, `Apartar` y `Apartado-listo`.
- Trazabilidad: RF-30 · R-14 · Entregable 1 §6, §7 y §11 (Sprint 4).
- **Se cumple con lo que ya se construyó** en HU-23 (el pedido nace con el punto, el día y la hora), HU-24 (aceptar y entregar) y HU-52 (apartar con hora y punto). Esta spec solo concilia la evidencia; no agrega código.

**Depende de:** `HU-23`, `HU-24`, `HU-52`.

## Requisitos
1. **R1 · Elegir:** al generar un pedido (o al apartar) se elige un punto de la lista del campus, con día y hora. La lista es `meetingPoints` (`db/014-pedidos.js`: los 14 edificios de la UTTT) y `GET /orders/meeting-points` sugiere el edificio de la carrera de quien compra.
2. **R2 · Registrar y ver:** el punto y la fecha quedan en el pedido (`delivery { pointSlug, pointName, detail?, at }`) o en el apartado (`pointSlug`, `pointName`, `pickupAt`) y los ven las dos partes.
3. **R3 · Límites:** solo dentro del campus, de 7:00 a 21:00 y en los próximos 14 días (pedido) o 24 h (apartado). Sin envíos a domicilio ni logística externa.

## Diseño
Sin cambios: `Modules/Orders/` (`OrderService`, `IMeetingPointRepository` con `MeetingPointDirectory`, y `ReservationService`). Filas de HU-23 y HU-52 en [`docs/endpoints.md`](../../../docs/endpoints.md).

## Contratos
`GET /api/v1/orders/meeting-points`, `POST /api/v1/orders` (`delivery`) y `POST /api/v1/reservations` (`meetingPoint`, `pickupAt`); errores `orders.invalid_meeting_point`, `orders.invalid_delivery_time` y `reservations.*` de [`docs/errores.md`](../../../docs/errores.md).

## Tests
Los de HU-23 y HU-52 (abajo, por criterio). No hay pruebas nuevas.

## Criterios
- [x] Al generar o confirmar un pedido se elige un punto de encuentro de una lista definida dentro de la universidad, junto con fecha y hora → `UnitTest/Orders/OrderServiceTests.cs` (`MeetingPoints_AreTheCampusList_WithMyCareerBuildingSuggested`, `Create_WithAPointThatIsNotOnTheCampusList_IsInvalid`, `Create_OutsideTheDeliveryWindow_IsInvalid`, `Create_At7And21OfTheCampus_AndIn14Days_AreAllowed`), `IntegrationTest/Orders/OrderFlowTests.cs` (`MeetingPoints_AreTheFourteenCampusBuildings`, `Quantity_Point_AndTime_AreValidated`) y HU-52 (`ReservationServiceTests`).
- [x] El punto de encuentro y la fecha quedan registrados en el pedido y son visibles para ambas partes → `OrderFlowTests.FromAListingChat_TheBuyerCreatesAPendingOrder_PaysCash_AndBothSeeIt`, `OrderLifecycleFlowTests.AcceptThenBothConfirm_CompletesTheOrder_AndTheListingIsSold`.
- [x] Los envíos a domicilio y la logística externa quedan fuera del alcance → no existe otra forma de entrega en el contrato (`delivery.pointSlug` es obligatorio y de la lista).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % (los de `main`) · `check_endpoints` OK · critic PASS.

## Límites
- **Decisión (16-Nov-2026, la propuesta de Jira):** la lista de puntos la administra quien administra el campus. Hoy la siembra el script (`db/014`); editarla desde la app queda para HU-41 (administración del catálogo), junto con las categorías y las carreras.
- Cambiar el punto o la hora de un pedido ya creado se acuerda por el chat y se cancela/rehace el pedido (no hay "reprogramar").

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 16-Nov-2026 (conciliada con HU-23, HU-24 y HU-52)
