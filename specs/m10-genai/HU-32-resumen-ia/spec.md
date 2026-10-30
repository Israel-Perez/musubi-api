# HU-32 — Pedir un resumen de la conversación con IA

**HU / Jira / Sprint / Módulo / Contraparte:** HU-32 / SCRUM-52 / Sprint 4 (9 – 20 nov 2026) / M-10 / [musubi-app:HU-32](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-32-resumen-ia)

## Contexto
Jira (SCRUM-52): Como **usuario**, quiero **pedir un resumen de la conversación**, para **recordar lo que se ha platicado**.

- Maqueta: `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-44 · CU-16 · Flujo E · RN-39, RN-40 · RNF-06, RNF-12, RNF-15.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-20`.

## Requisitos
1. La IA solo se ejecuta cuando un participante lo solicita; no analiza conversaciones de forma automática (RN-39).
2. Ambos participantes saben que la conversación puede procesarse con IA (RN-40).
3. Solo se envía al servicio de IA la información mínima necesaria (RNF-06).
4. Se muestra un indicador de proceso mientras se genera el resumen (RNF-12).
5. Si el servicio de IA no está disponible, se informa y el chat sigue funcionando (RNF-15).

RNF que aplican: RNF-06, RNF-12, RNF-15.

## Diseño
- Módulo `Musubi.Api/Modules/GenAi/` (M-10). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `agreements` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- IA detrás de una interfaz con adaptador intercambiable; si el proveedor falla, el resto funciona (RNF-15). Solo a petición de una persona (RNF-06).

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/GenAi/`: reglas de negocio de cada criterio.
- `IntegrationTest/GenAi/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] La IA solo se ejecuta cuando un participante lo solicita; no analiza conversaciones de forma automática (RN-39).
- [ ] Ambos participantes saben que la conversación puede procesarse con IA (RN-40).
- [ ] Solo se envía al servicio de IA la información mínima necesaria (RNF-06).
- [ ] Se muestra un indicador de proceso mientras se genera el resumen (RNF-12).
- [ ] Si el servicio de IA no está disponible, se informa y el chat sigue funcionando (RNF-15).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): mecanismo de consentimiento (RN-40) y tiempo de conservación de datos procesados por la IA (RN-44).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
