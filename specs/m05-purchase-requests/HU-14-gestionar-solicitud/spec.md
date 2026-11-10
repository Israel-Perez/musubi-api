# HU-14 — Editar, cerrar o cancelar mi solicitud

**HU / Jira / Sprint / Módulo / Contraparte:** HU-14 / SCRUM-36 / Sprint 4 (9 – 20 nov 2026) / M-05 / [musubi-app:HU-14](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-14-gestionar-solicitud)

## Contexto
Jira (SCRUM-36): Como **comprador**, quiero **editar, cerrar o cancelar mi solicitud**, para **que refleje si todavía necesito el producto**.

- Maqueta: `Mis-solicitudes` ("Cambiar" sobre la solicitud elegida) y `Necesidad-1…3` para editar.
- Trazabilidad: RF-17, RF-18 · CU-09 · RN-10, RN-11, RN-15.

**Depende de:** `HU-13` (solicitudes), `HU-15` (tablero) y `HU-16` (ofertas).

## Requisitos
1. **R1 · Editar:** quien publicó una solicitud activa cambia todos sus datos con las reglas de publicar (RN-09). Si ya recibió ofertas, el producto y la categoría no cambian. Solo se modera el texto que cambió (02-07). El vencimiento se recalcula (RN-10): al terminar la fecha nueva, o a los 30 días de publicarse.
2. **R2 · Cerrar ("Ya lo conseguí"):** la solicitud activa queda `closed`.
3. **R3 · Cancelar:** la solicitud activa queda `cancelled`.
4. **R4 · Solo activas (RN-11):** una cerrada, cancelada o vencida ya no se edita, cierra ni cancela (`409 wanted.not_active`). Cerrar una cerrada o cancelar una cancelada responde igual (idempotente). Una que no está activa sale del tablero, no cuenta para el máximo de 5 (RN-12) y no recibe ofertas (HU-16) ni se pueden aceptar las que tenía (HU-17).
5. **R5 · Vence sola (RF-18):** el estado `expired` se calcula al leer con `expiresAt` (HU-13); no hace falta un proceso.
6. **R6 · Solo quien la publicó (RNF-03, OWASP API1):** para otra persona, editar, cerrar o cancelar responde `404 wanted.not_found` y nada cambia. Una cuenta no activa recibe 403.

## Diseño
- **`WantedService`:** `UpdateAsync`, `CloseAsync` y `CancelAsync`. La validación de publicar pasa a `ValidateAsync` (la usan publicar y editar) y la moderación a `ModerateAsync` (al editar, solo lo que cambió).
- **Repositorio:** `IWantedRepository.UpdateDetailsAsync` y `SetStatusAsync`. Quien busca, `active` y `expiresAt > ahora` van en el filtro de cada escritura (`FindOneAndUpdate`), sin leer antes. Si cambia el producto, el filtro también pide `offersCount = 0`: una oferta que llega entre la lectura y la escritura gana.
- **Rutas:** `PUT /api/v1/wanted/{id}` (tope de 16 KB), `POST .../close` y `POST .../cancel` en `WantedController`.
- **Caché:** cada cambio borra el tablero del visitante (`wanted-board`).
- **Datos:** sin script nuevo; el `$jsonSchema` de `db/009` ya admitía `closed` y `cancelled`. Un opcional vacío se quita con `$unset` (el esquema no acepta `null`).

## Contratos
- Filas `PUT /api/v1/wanted/{id}`, `POST /api/v1/wanted/{id}/close` y `POST /api/v1/wanted/{id}/cancel` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Errores `wanted.not_active` y `wanted.has_offers` en [`docs/errores.md`](../../../docs/errores.md).
- Las tres responden `WantedResponse` (fixture `ContractTest/Fixtures/wanted.json`).

## Tests
- `UnitTest/PurchaseRequests/WantedServiceTests.cs`: R1–R6 (vencimiento nuevo, reglas de publicar, ofertas, moderación de lo que cambió, solo el dueño, cerrar, cancelar y vencida).
- `TestShared/PurchaseRequests/WantedRepositoryContract.cs`: `UpdateDetails_*` y `SetStatus_*`, en memoria y en MongoDB.
- `IntegrationTest/PurchaseRequests/WantedFlowTests.cs`: editar lo ve el campus; 400 y `has_offers`; cerrar la saca del tablero y ya no recibe ofertas; cancelar.
- `SecurityTest/Auth/ManageWantedAuthTests.cs`: 401, 404 para otra persona sin cambios, id inválido, cuenta suspendida y tope del cuerpo; `WantedAuthTests` suma las 3 rutas.
- `ContractTest/WantedContractTests.cs`: `EditCloseAndCancel_MatchTheFixture`.

## Criterios
- [x] El comprador puede editar, cerrar (ya consiguió el producto) o cancelar sus solicitudes (R1–R3).
- [x] Los estados son **Activa**, **Cerrada**, **Cancelada** y **Expirada**; solo las activas reciben ofertas (R4, RN-11).
- [x] La solicitud pasa a **Expirada** automáticamente al vencer su fecha límite o el periodo máximo (R5, RF-18; desde HU-13).
- [ ] Al completarse un pedido originado en la solicitud, el sistema propone cerrarla (RN-15): `NOTE (HU-25)`, porque todavía no hay pedidos.
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.
- [ ] `DatabaseTest` en el CI.

## Límites
- `NOTE (HU-25)`: proponer cerrar la solicitud al completarse su pedido (RN-15).
- `NOTE (HU-30)`: avisar a quienes ofrecieron que la solicitud se cerró o se canceló.
- No se reabre una solicitud cerrada, cancelada o vencida: se publica otra (la maqueta no tiene "Reabrir").

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 09-Nov-2026 (implementación)
- **Detalle:** con ofertas el producto y la categoría quedan fijos; las ofertas pendientes de una solicitud cerrada no se tocan (siguen visibles para quien la publicó, pero ya no se aceptan).
