# HU-21 — Ver el contexto de la conversación

**HU / Jira / Sprint / Módulo / Contraparte:** HU-21 / SCRUM-30 / Sprint 2 (12 – 23 oct 2026) / M-07 / [musubi-app:HU-21](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m07-chat/HU-21-contexto-conversacion)

## Contexto
Jira (SCRUM-30): Como **usuario**, quiero **que la conversación muestre el producto o la solicitud de la que se está hablando**, para **no perder el contexto**.

- Maqueta: `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-28 · CU-15.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-20`.

## Requisitos
1. Cada conversación muestra el producto, la solicitud o la oferta que la originó.
2. Desde ese contexto se puede abrir el detalle del elemento.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Chat/` (M-07). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `conversations`, `messages` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Chat/`: reglas de negocio de cada criterio.
- `IntegrationTest/Chat/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Cada conversación muestra el producto, la solicitud o la oferta que la originó.
- [ ] Desde ese contexto se puede abrir el detalle del elemento.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
