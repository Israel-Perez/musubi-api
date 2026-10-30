# HU-44 — Coordinar la entrega en un punto del campus

**HU / Jira / Sprint / Módulo / Contraparte:** HU-44 / SCRUM-56 / Sprint 4 (9 – 20 nov 2026) / M-08 / [musubi-app:HU-44](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-44-coordinar-entrega)

## Contexto
Jira (SCRUM-56): Como **comprador o vendedor**, quiero **acordar un punto de encuentro dentro del campus para la entrega**, para **concretar la operación de forma segura sin salir de la aplicación**.

- Maqueta: `Apartar`, `Apartado-listo`, `Mis-apartados`, `Verificar-comprador`, `Apartado-verificado`, `Pedido-pago`, `Pago-transferencia`, `Pago-validando`, `Pago-vendedor`, `Pedido-entrega`, `Pedido-efectivo`.
- Trazabilidad: RF-30 · R-14 · Entregable 1 §6, §7 y §11 (Sprint 4).
- Origen: Entregable 1 §6–7 — historia nueva, no estaba en la lista original.

**Depende de:** `02-01`, `02-03`, `HU-23`.

## Requisitos
1. Al generar o confirmar un pedido se elige un punto de encuentro de una lista definida dentro de la universidad, junto con fecha y hora.
2. El punto de encuentro y la fecha quedan registrados en el pedido y son visibles para ambas partes.
3. Los envíos a domicilio y la logística externa quedan fuera del alcance.

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
- [ ] Al generar o confirmar un pedido se elige un punto de encuentro de una lista definida dentro de la universidad, junto con fecha y hora.
- [ ] El punto de encuentro y la fecha quedan registrados en el pedido y son visibles para ambas partes.
- [ ] Los envíos a domicilio y la logística externa quedan fuera del alcance.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): quién administra la lista de puntos de encuentro (propuesta: el administrador).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
