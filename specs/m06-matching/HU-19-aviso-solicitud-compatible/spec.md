# HU-19 — Aviso al vendedor de solicitud compatible

**HU / Jira / Sprint / Módulo / Contraparte:** HU-19 / SCRUM-41 / Sprint 3 (26 oct – 6 nov 2026) / M-06 / [musubi-app:HU-19](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m06-matching/HU-19-aviso-solicitud-compatible)

## Contexto
Jira (SCRUM-41): Como **vendedor**, quiero **que el sistema me avise cuando una solicitud sea compatible con alguno de mis productos**, para **ofrecerlo a tiempo**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-23, RF-39 · CU-13, CU-14 · RN-28 a RN-31.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-18`, `HU-31`.

## Requisitos
1. Cuando se publica una solicitud compatible con un producto **Disponible**, el vendedor recibe una notificación (RF-39).
2. Solo se notifican coincidencias que superen el umbral de relevancia (RN-28).
3. Una misma coincidencia no se notifica más de una vez (RN-29) y se respeta el límite diario (RN-30).
4. No se notifican coincidencias sobre publicaciones o solicitudes propias (RN-31).
5. Desde la notificación el vendedor abre la solicitud y puede responder "Yo tengo este producto".

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Matching/` (M-06). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `offers` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Avisos por FCM vía el módulo de notificaciones (HU-31), en segundo plano (`outbox`, R14).

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Matching/`: reglas de negocio de cada criterio.
- `IntegrationTest/Matching/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Cuando se publica una solicitud compatible con un producto **Disponible**, el vendedor recibe una notificación (RF-39).
- [ ] Solo se notifican coincidencias que superen el umbral de relevancia (RN-28).
- [ ] Una misma coincidencia no se notifica más de una vez (RN-29) y se respeta el límite diario (RN-30).
- [ ] No se notifican coincidencias sobre publicaciones o solicitudes propias (RN-31).
- [ ] Desde la notificación el vendedor abre la solicitud y puede responder "Yo tengo este producto".
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
