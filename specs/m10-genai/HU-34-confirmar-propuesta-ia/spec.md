# HU-34 — Revisar y confirmar la propuesta de la IA

**HU / Jira / Sprint / Módulo / Contraparte:** HU-34 / SCRUM-54 / Sprint 4 (9 – 20 nov 2026) / M-10 / [musubi-app:HU-34](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-34-confirmar-propuesta-ia)

## Contexto
Jira (SCRUM-54): Como **usuario**, quiero **revisar y corregir la propuesta de la IA antes de confirmarla**, para **asegurarme de que refleja lo que realmente acordamos**.

- Maqueta: `Mensajes`, `Chat`, `Acuerdo-esperando`, `Acuerdo-confirmado`.
- Trazabilidad: RF-46, RF-47 · CU-16 · RN-41.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-33`.

## Requisitos
1. Ambos participantes pueden revisar y corregir la propuesta.
2. Un participante la envía al otro, que la confirma o pide cambios; si la rechaza, se descarta y la conversación continúa.
3. El acuerdo solo es válido cuando **ambos** lo confirman (RN-41).
4. Un acuerdo confirmado se puede convertir en pedido con los datos ya llenados (RF-47).

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
- [ ] Ambos participantes pueden revisar y corregir la propuesta.
- [ ] Un participante la envía al otro, que la confirma o pide cambios; si la rechaza, se descarta y la conversación continúa.
- [ ] El acuerdo solo es válido cuando **ambos** lo confirman (RN-41).
- [ ] Un acuerdo confirmado se puede convertir en pedido con los datos ya llenados (RF-47).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
