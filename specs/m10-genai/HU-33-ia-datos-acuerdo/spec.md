# HU-33 — La IA identifica los datos del acuerdo

**HU / Jira / Sprint / Módulo / Contraparte:** HU-33 / SCRUM-53 / Sprint 4 (9 – 20 nov 2026) / M-10 / [musubi-app:HU-33](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-33-ia-datos-acuerdo)

## Contexto
Jira (SCRUM-53): Como **comprador o vendedor**, quiero **que la IA identifique producto, precio, cantidad, entrega y fecha a partir del chat**, para **convertir la conversación en una propuesta de acuerdo**.

- Maqueta: `Acuerdo-esperando` ("Propuesta generada con IA, revísala antes de confirmar", "No identificado" → "Agregar").
- Trazabilidad: RF-45 · CU-16 · RN-42, RN-43.
- Decisiones de la usuaria (10-Nov-2026): proveedor **Gemini, capa gratuita** (registrada contra L-15c en `docs/bitacora/lineamientos-materia.md`); RN-40: la primera vez la app avisa y el chat deja "Ana pidió un resumen con IA"; RN-44: del chat no se guarda nada, solo la propuesta, que se borra al crear el pedido, al descartarla o a los 30 días, y el aviso de privacidad lo dice (HU-47).

**Depende de:** `HU-32`, `HU-23` (reglas del pedido y puntos de entrega).

## Requisitos
1. **R1 · Propuesta:** la IA devuelve producto, precio por pieza, cantidad, punto de entrega (uno de la lista del campus), detalle del lugar, día, hora y condiciones.
2. **R2 · "No identificado" (RN-42):** lo que no se mencionó llega `null`; además, cada valor pasa las reglas del pedido (cantidad de 1 a lo disponible, punto activo del campus, fecha de 30 minutos a 14 días entre las 7:00 y las 21:00, precio de $1 a $999,999) y la moderación, y si no, queda `null`. `notIdentified` guarda lo que faltó para que la app marque después "Agregada". Sin producto, se usa el de la conversación.
3. **R3 · La IA no actúa (RN-43):** solo devuelve JSON; las instrucciones le prohíben negociar o escribir mensajes e ignorar órdenes dentro del chat. Nada de lo que responde envía mensajes, confirma ni crea pedidos.
4. **R4 · Misma forma siempre:** la respuesta se valida antes de usarla (`ExtractionJson`): los ocho campos, cada uno de su tipo o `null`; si no, `genai.unavailable`.

## Diseño
Ver [`plan.md`](plan.md).

## Contratos
`AgreementResponse` en la fila `POST /api/v1/agreements` de [`docs/endpoints.md`](../../../docs/endpoints.md#m-10--ia-generativa); fixture `ContractTest/Fixtures/agreement.json`.

## Tests
- `UnitTest/GenAi/AgreementServiceTests.cs`: propuesta completa y "No identificado" por falta o por romper una regla.
- `UnitTest/GenAi/AgreementExtractionTests.cs`: respuestas con otra forma, valores sin sentido y las instrucciones del sistema.
- `ContractTest/AgreementsContractTests.cs`: forma de `AgreementResponse` y del aviso.

## Criterios
- [x] La IA propone producto, precio acordado, cantidad, lugar de entrega, fecha y condiciones (R1).
- [x] Lo que no se mencionó se marca como "No identificado" (R2, RN-42).
- [x] La IA no negocia, no envía mensajes y no acepta ni rechaza tratos (R3, RN-43).
- [x] La respuesta se valida antes de mostrarla (R4).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- La calidad de la extracción depende del modelo; se midió con un chat de prueba (1.2 s, 234 tokens, sin inventar la fecha que solo se preguntó). La persona siempre revisa antes de confirmar (HU-34).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 10-Nov-2026 (detallado e implementado)
- **Detalle:** salida JSON con esquema y validación propia; cada valor pasa las reglas del pedido.
