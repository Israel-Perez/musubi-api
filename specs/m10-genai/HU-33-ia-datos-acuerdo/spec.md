# HU-33 — La IA identifica los datos del acuerdo

**HU / Jira / Sprint / Módulo / Contraparte:** HU-33 / SCRUM-53 / Sprint 4 (9 – 20 nov 2026) / M-10 / [musubi-app:HU-33](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-33-ia-datos-acuerdo)

## Contexto
Jira (SCRUM-53): Como **comprador o vendedor**, quiero **que la IA identifique producto, precio, cantidad, entrega y fecha a partir del chat**, para **convertir la conversación en una propuesta de acuerdo**.

- Maqueta: `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-45 · CU-16 · RN-42, RN-43.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-32`.

## Requisitos
1. La IA propone producto, precio acordado, cantidad, lugar de entrega, fecha y condiciones.
2. Lo que no se mencionó en la conversación se marca como **"No identificado"** (RN-42).
3. La IA no negocia, no envía mensajes en nombre de los usuarios y no acepta ni rechaza tratos (RN-43).
4. La aplicación valida que la respuesta contenga siempre los mismos campos antes de mostrarla.

RNF que aplican: se confirman al detallar.

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
- [ ] La IA propone producto, precio acordado, cantidad, lugar de entrega, fecha y condiciones.
- [ ] Lo que no se mencionó en la conversación se marca como **"No identificado"** (RN-42).
- [ ] La IA no negocia, no envía mensajes en nombre de los usuarios y no acepta ni rechaza tratos (RN-43).
- [ ] La aplicación valida que la respuesta contenga siempre los mismos campos antes de mostrarla.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
