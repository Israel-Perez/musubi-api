# HU-34 — Revisar y confirmar la propuesta de la IA

**HU / Jira / Sprint / Módulo / Contraparte:** HU-34 / SCRUM-54 / Sprint 4 (9 – 20 nov 2026) / M-10 / [musubi-app:HU-34](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-34-confirmar-propuesta-ia)

## Contexto
Jira (SCRUM-54): Como **usuario**, quiero **revisar y corregir la propuesta de la IA antes de confirmarla**, para **asegurarme de que refleja lo que realmente acordamos**.

- Maqueta: `Acuerdo-esperando` (lápices, "0 de 2", "Confirmar acuerdo") y `Acuerdo-confirmado` ("Si algo cambia, ambos deben confirmar de nuevo", "Crear pedido").
- Trazabilidad: RF-46, RF-47 · CU-16 · RN-41.
- Decisiones de la usuaria (10-Nov-2026): proveedor **Gemini, capa gratuita** (registrada contra L-15c en `docs/bitacora/lineamientos-materia.md`); RN-40: la primera vez la app avisa y el chat deja "Ana pidió un resumen con IA"; RN-44: del chat no se guarda nada, solo la propuesta, que se borra al crear el pedido, al descartarla o a los 30 días, y el aviso de privacidad lo dice (HU-47).

**Depende de:** `HU-33`, `HU-23`.

## Requisitos
1. **R1 · Corregir:** cualquiera de los dos cambia producto, precio, cantidad, punto, detalle, fecha o condiciones (`PUT /agreements/{id}`), con las reglas del pedido y la moderación. Cada cambio sube `revision` y borra las dos confirmaciones.
2. **R2 · Confirmar (RN-41):** `POST /agreements/{id}/confirm` con la `revision` que se vio; vale solo si nada cambió (`agreements.changed`), si no falta nada (`agreements.incomplete`) y si la fecha sigue en la ventana. Con los dos sobre la misma versión queda `confirmed`.
3. **R3 · Descartar:** `DELETE /agreements/{id}` lo borra para los dos y la conversación sigue. Pedir cambios es corregir (R1).
4. **R4 · Pedido (RF-47):** `POST /orders` con `agreementId`: solo quien compra, solo un acuerdo confirmado; el pedido toma producto, precio, cantidad, punto, detalle y fecha del acuerdo y el acuerdo se borra.
5. **R5 · Tiempo real:** cada cambio emite `agreementChanged` a los dos.

## Diseño
Ver [`HU-33/plan.md`](../HU-33-ia-datos-acuerdo/plan.md).

## Contratos
Filas `GET`, `PUT`, `POST …/confirm` y `DELETE /api/v1/agreements…` y `POST /api/v1/orders` (`agreementId`) de [`docs/endpoints.md`](../../../docs/endpoints.md#m-10--ia-generativa).

## Tests
- `UnitTest/GenAi/AgreementServiceTests.cs`: corregir, reglas, confirmar la misma versión, fecha vencida, descartar y otro resumen.
- `UnitTest/Orders/OrderServiceTests.cs`: el pedido desde el acuerdo y sus errores.
- `TestShared/GenAi/AgreementRepositoryContract.cs`: versión, confirmaciones y borrado (memoria y Mongo).
- `IntegrationTest/GenAi/AgreementFlowTests.cs`: pedir, corregir, confirmar los dos, crear el pedido y descartar.

## Criterios
- [x] Ambos participantes pueden revisar y corregir la propuesta (R1).
- [x] Un participante la envía al otro, que la confirma o pide cambios; si la rechaza, se descarta y la conversación continúa (R2, R3, R5).
- [x] El acuerdo solo es válido cuando ambos lo confirman (R2, RN-41).
- [x] Un acuerdo confirmado se convierte en pedido con los datos ya llenados (R4, RF-47).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- `NOTE (HU-24)`: quien vende acepta el pedido que sale del acuerdo, igual que cualquier pedido.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 10-Nov-2026 (detallado e implementado)
- **Detalle:** confirmación por versión, descarte y pedido con lo acordado.
