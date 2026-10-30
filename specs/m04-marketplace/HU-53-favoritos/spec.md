# HU-53 — Guardar productos en favoritos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-53 / SCRUM-68 / por asignar / M-04 / [musubi-app:HU-53](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-53-favoritos)

## Contexto
Jira (SCRUM-68): Como **comprador**, quiero **guardar productos en favoritos**, para **volver a encontrarlos rápido y decidir después**.

- Maqueta: `Favoritos`, `Producto`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (sección 10, G-06).

**Depende de:** `02-01`, `02-03`, `HU-11`.

## Requisitos
1. Desde el detalle o un listado, el ícono de favorito agrega o quita el producto.
2. La pantalla "Favoritos" lista lo guardado e indica si un producto se agotó o se eliminó.
3. Los favoritos son privados: nadie más los ve (RNF-07).

RNF que aplican: RNF-07.

## Diseño
- Módulo `Musubi.Api/Modules/Marketplace/` (M-04). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `favorites` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
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
- [ ] Desde el detalle o un listado, el ícono de favorito agrega o quita el producto.
- [ ] La pantalla "Favoritos" lista lo guardado e indica si un producto se agotó o se eliminó.
- [ ] Los favoritos son privados: nadie más los ve (RNF-07).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): si los favoritos funcionan sin conexión (caché local de la app).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
