# HU-15 — Explorar solicitudes publicadas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-15 / SCRUM-37 / Sprint 3 (26 oct – 6 nov 2026) / M-05 / [musubi-app:HU-15](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-15-explorar-solicitudes)

## Contexto
Jira (SCRUM-37): Como **vendedor**, quiero **consultar y filtrar las solicitudes publicadas**, para **encontrar oportunidades de venta**.

- Maqueta: `Inicio-visitante`, `Se-busca-visitante`, `Producto-visitante`, `Visitante-modal`, `Se-busca`, `Yo-tengo-vincular`, `Yo-tengo-oferta`, `Yo-tengo-enviada`.
- Trazabilidad: RF-19 · CU-10 · RNF-19.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-13`.

## Requisitos
1. Se listan las solicitudes **Activas**.
2. Se pueden filtrar por categoría y presupuesto.
3. Las solicitudes se distinguen visualmente de los productos publicados (RNF-19).

RNF que aplican: RNF-19.

## Diseño
- Módulo `Musubi.Api/Modules/PurchaseRequests/` (M-05). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `wantedRequests` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/PurchaseRequests/`: reglas de negocio de cada criterio.
- `IntegrationTest/PurchaseRequests/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Se listan las solicitudes **Activas**.
- [ ] Se pueden filtrar por categoría y presupuesto.
- [ ] Las solicitudes se distinguen visualmente de los productos publicados (RNF-19).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
