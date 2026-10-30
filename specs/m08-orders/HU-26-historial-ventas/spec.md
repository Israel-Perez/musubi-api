# HU-26 — Consultar mi historial de ventas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-26 / SCRUM-45 / Sprint 3 (26 oct – 6 nov 2026) / M-08 / [musubi-app:HU-26](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-26-historial-ventas)

## Contexto
Jira (SCRUM-45): Como **vendedor**, quiero **consultar mi historial de ventas**, para **saber qué he vendido**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-33 · CU-18.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-23`.

## Requisitos
1. El vendedor consulta su historial de ventas.
2. Cada venta muestra producto, comprador, cantidad, precio, fecha y estado.

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
- [ ] El vendedor consulta su historial de ventas.
- [ ] Cada venta muestra producto, comprador, cantidad, precio, fecha y estado.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
