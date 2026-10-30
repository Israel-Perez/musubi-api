# HU-23 — Confirmar la compra de un producto

**HU / Jira / Sprint / Módulo / Contraparte:** HU-23 / SCRUM-42 / Sprint 3 (26 oct – 6 nov 2026) / M-08 / [musubi-app:HU-23](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-23-confirmar-compra)

## Contexto
Jira (SCRUM-42): Como **comprador**, quiero **confirmar la compra de un producto**, para **formalizar el trato con el vendedor**.

- Maqueta: `Pedido-pago`, `Pago-transferencia`, `Pago-validando`, `Pago-vendedor`, `Pedido-entrega`, `Pedido-efectivo`.
- Trazabilidad: RF-30, RF-31, RF-34 · CU-17 · RN-06, RN-16, RN-17, RN-18.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-11`, `HU-20`.

## Requisitos
1. El comprador genera un pedido desde el detalle de un producto, desde una oferta aceptada o desde un acuerdo confirmado.
2. El pedido registra producto, cantidad, precio, lugar y fecha de entrega.
3. Todo pedido inicia como **Pendiente** (RN-16).
4. La cantidad no puede superar la disponible (RN-17) y no se pueden comprar productos propios (RN-06).
5. En Sprint 3 el pago se realiza al momento de la entrega y queda registrado en el pedido (Entregable 1).

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
- [ ] El comprador genera un pedido desde el detalle de un producto, desde una oferta aceptada o desde un acuerdo confirmado.
- [ ] El pedido registra producto, cantidad, precio, lugar y fecha de entrega.
- [ ] Todo pedido inicia como **Pendiente** (RN-16).
- [ ] La cantidad no puede superar la disponible (RN-17) y no se pueden comprar productos propios (RN-06).
- [ ] En Sprint 3 el pago se realiza al momento de la entrega y queda registrado en el pedido (Entregable 1).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): El pago digital con Mercado Pago se agrega en HU-43 (Sprint 4).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
