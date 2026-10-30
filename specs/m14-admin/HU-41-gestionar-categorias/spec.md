# HU-41 — Gestionar categorías

**HU / Jira / Sprint / Módulo / Contraparte:** HU-41 / SCRUM-64 / Sprint 5 (23 nov – 4 dic 2026) / M-14 / [musubi-app:HU-41](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-41-gestionar-categorias)

## Contexto
Jira (SCRUM-64): Como **administrador**, quiero **gestionar las categorías**, para **mantener organizada la plataforma**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-56 · CU-26.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-06`.

## Requisitos
1. El administrador puede crear, editar y desactivar categorías.
2. Las categorías activas son las que aparecen al publicar productos y solicitudes, y en los filtros.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Admin/` (M-14). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `categories` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Admin/`: reglas de negocio de cada criterio.
- `IntegrationTest/Admin/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El administrador puede crear, editar y desactivar categorías.
- [ ] Las categorías activas son las que aparecen al publicar productos y solicitudes, y en los filtros.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): Desde Sprint 2 se requiere un catálogo inicial de categorías cargado en la base de datos.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
