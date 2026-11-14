# HU-30 — Aviso de oferta en mi solicitud

**HU / Jira / Sprint / Módulo / Contraparte:** HU-30 / SCRUM-48 / Sprint 4 (9 – 20 nov 2026) / M-09 / [musubi-app:HU-30](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m09-notifications/HU-30-aviso-oferta-solicitud)

## Contexto
Jira (SCRUM-48): Como **comprador**, quiero **recibir una notificación cuando alguien indique que tiene el producto que solicité**, para **responder rápido**.

- Maqueta: `Notificaciones`: "Alguien tiene lo que buscas · Diego respondió “Yo tengo este producto” a tu solicitud de Calculadora Casio · $500."
- Trazabilidad: RF-40 · CU-11, CU-14.
- Decisión de la usuaria (13-Nov-2026): también se avisa a quien tenía una oferta pendiente cuando la solicitud se cierra o se cancela.

**Depende de:** `HU-16`, `HU-17` y `HU-31`.

## Requisitos
1. **R1 · Aviso de oferta:** cada oferta nueva le llega a quien busca (`offer_received`, desde HU-31), con el destino `wanted` y el id de la solicitud.
2. **R2 · Abrir la oferta:** la app abre esa solicitud en `Mis-solicitudes`, con sus ofertas (`musubi-app:HU-30`).
3. **R3 · Cierre:** al cerrar ("Ya lo conseguí") o cancelar, cada persona con una oferta pendiente recibe `offer_closed` ("Esta solicitud ya no está activa") en "Solicitudes", una sola vez y sin destino (`none`). Las aceptadas ya tienen su chat y las rechazadas su aviso.

## Diseño
- `WantedService.SetStatusAsync` → `NotifyOffersClosedAsync` (lee las ofertas con `IOfferRepository.FindByRequestAsync`).
- Tipos `offer_closed` y destino `none` en `NotificationKinds.cs`.

## Contratos
- Sin rutas nuevas. La fila `GET /api/v1/notifications` de [`docs/endpoints.md`](../../../docs/endpoints.md) suma el tipo `offer_closed` y el destino `none`.

## Tests
- `UnitTest/PurchaseRequests/WantedServiceTests.cs`: `Close_TellsWhoHadAPendingOffer_Once` y `Cancel_TellsWhoHadAPendingOffer_ThatItWasCancelled`.
- `IntegrationTest/Matching/WantedMatchFlowTests.cs`: la oferta avisa a quien busca (destino `wanted` con la solicitud) y cerrarla avisa a quien ofreció.

## Criterios
- [x] Se notifica al comprador cada vez que alguien responde "Yo tengo este producto" → integración (`offer_received`).
- [x] Al tocar la notificación se abre la oferta dentro de la solicitud → destino `wanted` + id (la app lo abre, `musubi-app:HU-30`).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Una solicitud que vence sola (RN-10) no avisa a quien ofreció: no hay un evento al vencer.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 13-Nov-2026 (detallado e implementado)
- **Detalle:** con la decisión de la usuaria del 13-Nov-2026.
