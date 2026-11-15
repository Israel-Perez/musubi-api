# HU-52 — Apartar un producto y verificar la entrega con QR o código

**HU / Jira / Sprint / Módulo / Contraparte:** HU-52 / SCRUM-74 / Sprint 5 (14 nov – 1 dic 2026) / M-08 / [musubi-app:HU-52](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-52-apartar-producto)

## Contexto
Jira (SCRUM-74): Como **comprador**, quiero **apartar un producto por 24 horas y recibir un QR o un código**, para **que el vendedor me lo guarde y verifique que soy yo al entregarlo**.

- Maqueta: `Producto` ("Apártalo 24 h sin pagar" y "Apartar"), `Apartar`, `Apartado-listo`, `Mis-apartados`, `Verificar-comprador` y `Apartado-verificado`.
- Origen: maqueta (sección 4, G-05). Trazabilidad: RNF-03.
- **Decisiones de la usuaria (15-Nov-2026):**
  - **Un apartado activo** por comprador a la vez.
  - **QR y cámara completos**: `qr_flutter` y `mobile_scanner` en la app, con su excepción en 00-01 y el permiso de cámara.
  - **Al confirmar la entrega nace un pedido completado** (ventas, "Mis pedidos", publicación vendida y calificaciones).

**Depende de:** `HU-23` (pedidos), `HU-24` (`IProductStock` y los efectos de completar), `HU-44` (puntos de entrega), `HU-46` (borrar la cuenta).

## Requisitos
1. **R1 · Apartar:**
   - Se elige el punto del campus y la hora: dentro de las 24 h, entre las 7:00 y las 21:00.
   - Se guarda una pieza (resta de `products.stock`, como aceptar un pedido) y se abre la conversación de los dos ("Mensaje").
   - Se avisa a quien vende (`reservation_created`). El apartado vence a las 24 h.
   - No se aparta lo propio, lo agotado ni con un bloqueo (HU-57).
2. **R2 · QR y código:** quien aparta recibe un código de 6 dígitos (generador criptográfico) y un QR con `musubi:apartado:{id}:{code}`. Solo él los ve, y solo mientras el apartado está activo.
3. **R3 · Verificar:**
   - Solo quien vende de ese apartado escanea el QR o escribe el código (RNF-03).
   - Cada intento cuenta antes de comparar, hasta 5, y la comparación es en tiempo constante (contra la fuerza bruta).
4. **R4 · Entregar:** con el código verificado, "Confirmar entrega" pasa el apartado a recogido y crea un **pedido completado** en efectivo con la conversación de los dos. Tiene los efectos de HU-24/26/35 (publicación vendida si se acaban las piezas, ventas y compras del perfil, señal de compra) y se puede calificar (HU-27).
5. **R5 · Mis apartados:** "Yo aparté" y "Me apartaron", con los números de las dos.
6. **R6 · Cancelar y vencer:**
   - Cualquiera de las partes cancela (`reservation_cancelled`) y la pieza regresa.
   - A las 24 h sin recoger, `ReservationExpiryWorker` lo vence, regresa la pieza y avisa a los dos (`reservation_expired`).
   - Al borrar la cuenta, sus apartados activos se cancelan.
7. **R7 · Uno a la vez:** `ux_buyer_active` (único parcial) gana entre dos toques a la vez; si pierde, la pieza regresa (`reservations.limit_reached`).

## Diseño
- `Modules/Orders/`:
  - `ReservationService`, `ReservationExpiryJob`/`ReservationExpiryWorker`, `ReservationAccountEraser`;
  - `ReservationsController` (`/reservations`), `ReservationDtos.cs` y `ReservationsErrors`.
- `Data/ReservationRepositories.cs` y `db/028-apartados.js` (`reservations` con sus índices y el validador de avisos); esquema 28, API `0.5.0-alpha.36`.
- Contratos que se usan:
  - `IConversationOpener.OpenForProductAsync` (nuevo en M-07);
  - `OrderLifecycleService.AfterCompletedAsync` (los efectos de completar);
  - `IProductStock`, `IProductDirectory`, `IMeetingPointRepository` y `IBlockDirectory`.

## Contratos
Sección "Apartados (HU-52)" de [`docs/endpoints.md`](../../../docs/endpoints.md), errores `reservations.*` de [`docs/errores.md`](../../../docs/errores.md) y fixtures `reservation.json` y `reservation-page.json`.

## Tests
- `UnitTest/Orders/ReservationServiceTests.cs`: apartar (pieza, chat y aviso), el código solo para quien aparta, uno a la vez, punto y hora, lo propio, lo agotado y el bloqueo, verificar y entregar (pedido completado, publicación vendida), 5 intentos, cancelar, vencer, las pestañas y el borrado de la cuenta.
- `TestShared/Orders/ReservationRepositoryContract.cs` en memoria y en MongoDB real (`DatabaseTest`, `ux_buyer_active` y el validador de `db/028`).
- `IntegrationTest/Orders/ReservationFlowTests.cs`, `SecurityTest/Auth/ReservationsAuthTests.cs` y `ContractTest/ReservationsContractTests.cs`.

## Criterios
- [x] Al apartar se eligen la hora y el punto de entrega; vence a las 24 h → `ReservationServiceTests`, `ReservationFlowTests`.
- [x] El comprador recibe un QR y un código de 6 dígitos de un solo uso → `Reserving_KeepsThePiece…`, `OnlyTheBuyerSeesTheCode…`, `ReservationsAuthTests`.
- [x] El vendedor escanea el QR o escribe el código; solo el vendedor de ese apartado verifica → `VerifyingAndDelivering…`, `ReservationsAuthTests`.
- [x] "Mis apartados" muestra lo que aparté y lo que me apartaron → `MyReservations_SplitTheTabs…`, contrato.
- [x] El código no se adivina por fuerza bruta → `AWrongCode_SpendsAnAttempt_AndAfterFiveItLocks`.
- [x] Build Release 0/0 · Unit 840, Integration 193, Security 266 y Contract 28 · `check_endpoints` OK (112) · critic PASS.

## Límites
- La fecha de la maqueta es por botones ("Hoy 1:00 p. m."); la API acepta cualquier hora dentro de la ventana y la app arma las opciones.
- Sin estado "Reservada" en la publicación: un apartado resta la pieza como un pedido aceptado (sin piezas, agotada).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 15-Nov-2026 (detallado e implementado, con las decisiones de la usuaria)
