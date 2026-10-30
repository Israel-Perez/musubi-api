# HU-54 — Elegir modo noche y preferencias

**HU / Jira / Sprint / Módulo / Contraparte:** HU-54 / SCRUM-71 / por asignar / M-02 / [musubi-app:HU-54](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-54-modo-noche-preferencias)

## Contexto
Jira (SCRUM-71): Como **usuario**, quiero **cambiar entre modo claro y modo noche y ajustar mis preferencias**, para **usar la app con comodidad a cualquier hora**.

- Maqueta: `Ajustes`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (sección 10, G-07).

**Depende de:** `02-01`, `02-03`, `HU-04`.

## Requisitos
1. Ajustes ofrece el modo claro, el modo noche y "como el sistema".
2. El modo noche usa la paleta oscura de la guía de estilo y mantiene el contraste AA (RNF-20).
3. La preferencia se conserva al cerrar y abrir la app.

RNF que aplican: RNF-20.

## Diseño
- Módulo `Musubi.Api/Modules/Profiles/` (M-02). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `preferences` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Profiles/`: reglas de negocio de cada criterio.
- `IntegrationTest/Profiles/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Ajustes ofrece el modo claro, el modo noche y "como el sistema".
- [ ] El modo noche usa la paleta oscura de la guía de estilo y mantiene el contraste AA (RNF-20).
- [ ] La preferencia se conserva al cerrar y abrir la app.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): qué otras preferencias de la pantalla Ajustes entran al MVP (notificaciones por tipo).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
