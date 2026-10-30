# HU-20 — Iniciar una conversación con el vendedor

**HU / Jira / Sprint / Módulo / Contraparte:** HU-20 / SCRUM-29 / Sprint 2 (12 – 23 oct 2026) / M-07 / [musubi-app:HU-20](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m07-chat/HU-20-iniciar-conversacion)

## Contexto
Jira (SCRUM-29): Como **comprador**, quiero **iniciar una conversación con el vendedor desde la publicación o desde la oferta**, para **resolver dudas y negociar**.

- Maqueta: `Producto`, `Perfil-vendedor`, `Chat-Valeria`, `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-26, RF-27 · CU-15 · RN-21, RN-22, RN-23 · RNF-10.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-11`.

## Requisitos
1. Se puede iniciar un chat desde una publicación, una solicitud o una oferta.
2. Cada conversación es entre dos usuarios y está asociada a una publicación, solicitud u oferta (RN-21).
3. Los mensajes de texto se entregan casi en tiempo real (RNF-10).
4. Solo los participantes pueden leer la conversación (RN-22).
5. Los mensajes pueden reportarse (RN-23).

RNF que aplican: RNF-10.

## Diseño
- Módulo `Musubi.Api/Modules/Chat/` (M-07). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `conversations`, `messages` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Moderación antes de guardar el texto o las imágenes (L-16b): `NOTE (02-07)`.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Chat/`: reglas de negocio de cada criterio.
- `IntegrationTest/Chat/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Se puede iniciar un chat desde una publicación, una solicitud o una oferta.
- [ ] Cada conversación es entre dos usuarios y está asociada a una publicación, solicitud u oferta (RN-21).
- [ ] Los mensajes de texto se entregan casi en tiempo real (RNF-10).
- [ ] Solo los participantes pueden leer la conversación (RN-22).
- [ ] Los mensajes pueden reportarse (RN-23).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): En Sprint 2 se habilita desde publicaciones; el inicio desde solicitudes y ofertas se completa en Sprint 3.
- Decisión pendiente (Jira): envío de imágenes [POR DEFINIR] (RF-27).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
