# HU-08 — Consultar el estado de mis productos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-08 / SCRUM-25 / Sprint 2 (12 – 23 oct 2026) / M-03 / [musubi-app:HU-08](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-08-estado-mis-productos)

## Contexto
Jira (SCRUM-25): Como **vendedor**, quiero **consultar el estado de mis productos**, para **saber cuáles están disponibles, reservados o vendidos**.

- Maqueta: `Perfil`, `Mis-productos`.
- Trazabilidad: RF-10, RF-11 · CU-05 · RN-05.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-06`, `HU-23`.

## Requisitos
1. El vendedor ve la lista de sus publicaciones con su estado y el tiempo que llevan publicadas (RF-11).
2. Los estados posibles son **Disponible**, **Reservada**, **Vendida** e **Inactiva** (RF-10).
3. El estado cambia automáticamente según los pedidos; cuando la cantidad llega a cero pasa a **Vendida** (RN-05).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Listings/` (M-03). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `products` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Listings/`: reglas de negocio de cada criterio.
- `IntegrationTest/Listings/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El vendedor ve la lista de sus publicaciones con su estado y el tiempo que llevan publicadas (RF-11).
- [ ] Los estados posibles son **Disponible**, **Reservada**, **Vendida** e **Inactiva** (RF-10).
- [ ] El estado cambia automáticamente según los pedidos; cuando la cantidad llega a cero pasa a **Vendida** (RN-05).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
