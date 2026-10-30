# HU-28 — Reportar contenido o usuarios sospechosos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-28 / SCRUM-57 / Sprint 5 (23 nov – 4 dic 2026) / M-13 / [musubi-app:HU-28](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m13-reputation/HU-28-reportar-contenido)

## Contexto
Jira (SCRUM-57): Como **usuario**, quiero **reportar una publicación, solicitud o usuario sospechoso**, para **mantener segura la comunidad**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-37 · CU-20 · RN-23, RN-27.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-05`, `HU-11`.

## Requisitos
1. Se puede reportar una publicación, solicitud, oferta, mensaje o usuario, indicando el motivo.
2. El reporte llega a la revisión del administrador (HU-39).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Reputation/` (M-13). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `reports` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Moderación antes de guardar el texto o las imágenes (L-16b): `NOTE (02-07)`.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Reputation/`: reglas de negocio de cada criterio.
- `IntegrationTest/Reputation/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Se puede reportar una publicación, solicitud, oferta, mensaje o usuario, indicando el motivo.
- [ ] El reporte llega a la revisión del administrador (HU-39).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): Se ubica en Sprint 5 junto con el módulo de administración y moderación.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
