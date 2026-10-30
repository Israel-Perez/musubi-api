# HU-24 — Aceptar y actualizar mis pedidos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-24 / SCRUM-43 / Sprint 3 (26 oct – 6 nov 2026) / M-08 / [musubi-app:HU-24](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-24-actualizar-pedidos)

## Contexto
Jira (SCRUM-43): Como **vendedor**, quiero **aceptar y actualizar el estado de mis pedidos**, para **llevar control de las entregas**.

- Maqueta: `Pedido-pago`, `Pago-transferencia`, `Pago-validando`, `Pago-vendedor`, `Pedido-entrega`, `Pedido-efectivo`.
- Trazabilidad: RF-31, RF-33, RF-34 · CU-18 · RN-16, RN-19, RN-20.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-23`.

## Requisitos
1. El vendedor acepta o rechaza pedidos **Pendientes**; al aceptarlo pasa a **Confirmado** y la cantidad se aparta (RF-34).
2. Los estados del pedido son **Pendiente**, **Confirmado**, **Completado** y **Cancelado** (RF-31).
3. El pedido pasa a **Completado** cuando ambas partes confirman la entrega (RN-19, propuesta) y la cantidad se descuenta.
4. Cualquiera de las partes puede cancelar antes de completar, indicando el motivo; la cantidad se libera (RN-20).
5. Si la cantidad llega a cero, la publicación pasa a **Vendida**.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Orders/` (M-08). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `orders` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Orders/`: reglas de negocio de cada criterio.
- `IntegrationTest/Orders/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El vendedor acepta o rechaza pedidos **Pendientes**; al aceptarlo pasa a **Confirmado** y la cantidad se aparta (RF-34).
- [ ] Los estados del pedido son **Pendiente**, **Confirmado**, **Completado** y **Cancelado** (RF-31).
- [ ] El pedido pasa a **Completado** cuando ambas partes confirman la entrega (RN-19, propuesta) y la cantidad se descuenta.
- [ ] Cualquiera de las partes puede cancelar antes de completar, indicando el motivo; la cantidad se libera (RN-20).
- [ ] Si la cantidad llega a cero, la publicación pasa a **Vendida**.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): confirmar si basta con que el comprador confirme la entrega (RN-19).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
