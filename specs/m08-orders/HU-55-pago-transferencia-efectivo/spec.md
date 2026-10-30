# HU-55 — Pagar un pedido por transferencia o en efectivo

**HU / Jira / Sprint / Módulo / Contraparte:** HU-55 / SCRUM-72 / por asignar / M-08 / [musubi-app:HU-55](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-55-pago-transferencia-efectivo)

## Contexto
Jira (SCRUM-72): Como **comprador**, quiero **pagar un pedido por transferencia a la cuenta verificada del vendedor o en efectivo al recibirlo**, para **cerrar la compra sin comisiones**.

- Maqueta: `Pedido-pago`, `Pago-transferencia`, `Pago-validando`, `Pago-vendedor`, `Pedido-entrega`, `Pedido-efectivo`, `Cuenta-pagos`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (secciones 9 y 10). Decisión D-01 (21-Oct-2026): se sigue el modelo de pago de la maqueta y **reemplaza a HU-43** (Mercado Pago, fuera de alcance)..

**Depende de:** `02-01`, `02-03`, `HU-23`, `HU-44`.

## Requisitos
1. El vendedor registra su cuenta para recibir pagos (CLABE), que se valida y se guarda cifrada; solo se muestra enmascarada.
2. Por **transferencia**, el comprador ve la cuenta del vendedor y una referencia; el vendedor valida el pago y el pedido pasa a "Confirmado".
3. En **efectivo**, el comprador recibe un código de entrega de 4 dígitos que el vendedor captura al entregar; el pedido pasa a "Completado".
4. Musubi no cobra comisión ni procesa el dinero (se indica en la pantalla).
5. Solo las partes del pedido ven los datos de pago (RNF-03, RNF-05).

RNF que aplican: RNF-03, RNF-05.

## Diseño
- Módulo `Musubi.Api/Modules/Orders/` (M-08). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `orders`, `payoutAccounts` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
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
- [ ] El vendedor registra su cuenta para recibir pagos (CLABE), que se valida y se guarda cifrada; solo se muestra enmascarada.
- [ ] Por **transferencia**, el comprador ve la cuenta del vendedor y una referencia; el vendedor valida el pago y el pedido pasa a "Confirmado".
- [ ] En **efectivo**, el comprador recibe un código de entrega de 4 dígitos que el vendedor captura al entregar; el pedido pasa a "Completado".
- [ ] Musubi no cobra comisión ni procesa el dinero (se indica en la pantalla).
- [ ] Solo las partes del pedido ven los datos de pago (RNF-03, RNF-05).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): el plazo para validar una transferencia antes de cancelar el pedido.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
