# HU-10 — Filtrar por categoría y precio

**HU / Jira / Sprint / Módulo / Contraparte:** HU-10 / SCRUM-27 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-10](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-10-filtrar-categoria-precio)

## Contexto
Jira (SCRUM-27): Como **comprador**, quiero **filtrar los resultados por categoría y precio**, para **descartar lo que no me sirve**.

- Maqueta: `Busqueda`, `Busqueda-filtros`, `Busqueda-vacia`.
- Trazabilidad: RF-13 · CU-06.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-09`.

## Requisitos
1. Se puede filtrar por categoría.
2. Se puede filtrar por rango de precio.
3. Los filtros se combinan con la búsqueda por palabra clave.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Marketplace/` (M-04). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `products` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Marketplace/`: reglas de negocio de cada criterio.
- `IntegrationTest/Marketplace/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Se puede filtrar por categoría.
- [ ] Se puede filtrar por rango de precio.
- [ ] Los filtros se combinan con la búsqueda por palabra clave.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
