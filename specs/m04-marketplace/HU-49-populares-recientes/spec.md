# HU-49 — Ver populares y recién publicados

**HU / Jira / Sprint / Módulo / Contraparte:** HU-49 / SCRUM-69 / por asignar / M-04 / [musubi-app:HU-49](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-49-populares-recientes)

## Contexto
Jira (SCRUM-69): Como **comprador**, quiero **ver lo más popular de hoy y de la semana y lo recién publicado en mi campus**, para **descubrir productos sin tener que buscarlos**.

- Maqueta: `Inicio`, `Recien-publicados`, `Recien-publicados-antiguas`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (sección 3, G-02).

**Depende de:** `02-01`, `02-03`, `HU-11`.

## Requisitos
1. Inicio muestra "Populares" con un selector Hoy / Semana, calculado por las vistas de mi campus.
2. "Recién publicados" lista los productos disponibles del más nuevo al más antiguo y tiene la opción "Más antiguas".
3. Los listados están paginados y se pueden servir desde caché (lineamientos 9 y 15).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Marketplace/` (M-04). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `productStatsDaily` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
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
- [ ] Inicio muestra "Populares" con un selector Hoy / Semana, calculado por las vistas de mi campus.
- [ ] "Recién publicados" lista los productos disponibles del más nuevo al más antiguo y tiene la opción "Más antiguas".
- [ ] Los listados están paginados y se pueden servir desde caché (lineamientos 9 y 15).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): qué cuenta como "vista" para la popularidad (abrir el detalle) y cada cuánto se recalcula.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
