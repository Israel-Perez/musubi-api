# HU-07 — Editar o eliminar mis publicaciones

**HU / Jira / Sprint / Módulo / Contraparte:** HU-07 / SCRUM-24 / Sprint 2 (12 – 23 oct 2026) / M-03 / [musubi-app:HU-07](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-07-editar-eliminar-publicaciones)

## Contexto
Jira (SCRUM-24): Como **vendedor**, quiero **editar o eliminar mis publicaciones**, para **que la información siempre sea correcta**.

- Maqueta: `Perfil`, `Mis-productos`.
- Trazabilidad: RF-08, RF-09 · CU-05 · RN-07 · RNF-03.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-06`.

## Requisitos
1. Solo el dueño de la publicación puede editarla o eliminarla (RNF-03).
2. Si cambian el nombre, la categoría, el precio o la descripción, el sistema vuelve a buscar coincidencias (RF-08).
3. Una publicación con pedidos **Pendientes** o **Confirmados** no puede eliminarse hasta que se completen o cancelen (RN-07).

RNF que aplican: RNF-03.

## Diseño
- Módulo `Musubi.Api/Modules/Listings/` (M-03). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `products` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Moderación antes de guardar el texto o las imágenes (L-16b): `NOTE (02-07)`.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Listings/`: reglas de negocio de cada criterio.
- `IntegrationTest/Listings/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Solo el dueño de la publicación puede editarla o eliminarla (RNF-03).
- [ ] Si cambian el nombre, la categoría, el precio o la descripción, el sistema vuelve a buscar coincidencias (RF-08).
- [ ] Una publicación con pedidos **Pendientes** o **Confirmados** no puede eliminarse hasta que se completen o cancelen (RN-07).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
