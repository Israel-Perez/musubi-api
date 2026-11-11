# HU-32 — Pedir un resumen de la conversación con IA

**HU / Jira / Sprint / Módulo / Contraparte:** HU-32 / SCRUM-52 / Sprint 4 (9 – 20 nov 2026) / M-10 / [musubi-app:HU-32](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m10-genai/HU-32-resumen-ia)

## Contexto
Jira (SCRUM-52): Como **usuario**, quiero **pedir un resumen de la conversación**, para **recordar lo que se ha platicado**.

- Maqueta: `Chat` ("¿Ya se pusieron de acuerdo?" → "Resumir acuerdo") y `Acuerdo-esperando`.
- Trazabilidad: RF-44 · CU-16 · Flujo E · RN-39, RN-40, RN-44 · RNF-06, RNF-12, RNF-15.
- Decisiones de la usuaria (10-Nov-2026): proveedor **Gemini, capa gratuita** (registrada contra L-15c en `docs/bitacora/lineamientos-materia.md`); RN-40: la primera vez la app avisa y el chat deja "Ana pidió un resumen con IA"; RN-44: del chat no se guarda nada, solo la propuesta, que se borra al crear el pedido, al descartarla o a los 30 días, y el aviso de privacidad lo dice (HU-47).
- El "resumen" es la propuesta de acuerdo de HU-33: las tres HU comparten la ruta y la hoja, así que se entregan en un solo PR por repo.

**Depende de:** `02-01`, `02-03`, `HU-20` (chat), `HU-23` (pedido).

## Requisitos
1. **R1 · A petición (RN-39):** solo `POST /agreements`, que llama un participante, lee el chat. Nada lo dispara solo. Sin mensajes de texto no se llama a la IA (`agreements.nothing_to_summarize`).
2. **R2 · Los dos lo saben (RN-40):** en cuanto el chat sale hacia la IA, la conversación recibe el aviso `kind: notice` "<Nombre> pidió un resumen con IA" (cuenta como no leído del otro lado y llega por el hub).
3. **R3 · Lo mínimo (RNF-06):** salen los últimos 60 mensajes de texto de esa conversación, sin ids ni avisos, con los nombres de los dos cambiados por "Comprador" y "Vendedor" y sin correos, teléfonos, cuentas, enlaces ni usuarios (`TranscriptRedactor`). Sin llave de Gemini no sale nada.
4. **R4 · Degradación (RNF-15):** sin proveedor, sin red, tiempo agotado (20 s), cuota excedida (429), respuesta bloqueada o con otra forma: `503 genai.unavailable`, no se guarda nada y el chat, la búsqueda y los pedidos siguen igual. Solo se registra el motivo, nunca el chat.
5. **R5 · Cuota:** 6 resúmenes por persona por hora (`rate limit genai`, `429 rate_limit.exceeded`).
6. **R6 · Conservación (RN-44):** del chat no se guarda nada; el acuerdo vence a los 30 días (índice TTL).

RNF-12 (indicador mientras se genera) vive en la app.

## Diseño
- Módulo nuevo `Musubi.Api/Modules/GenAi/`. Proveedor detrás de `IAgreementExtractor`: `GeminiAgreementExtractor` (REST `generateContent`, `x-goog-api-key`, salida JSON con esquema, temperatura 0, `gemini-3.5-flash-lite`), `InMemoryAgreementExtractor` (Development y pruebas) y `OffAgreementExtractor`. Se elige con `GenAi:Provider`.
- El chat expone `IConversationNotices` (leer los últimos textos, dejar el aviso y emitir `agreementChanged`).
- Detalle completo en [`HU-33/plan.md`](../HU-33-ia-datos-acuerdo/plan.md).

## Contratos
Fila `POST /api/v1/agreements` de [`docs/endpoints.md`](../../../docs/endpoints.md#m-10--ia-generativa), `MessageResponse.kind` (M-07) y errores M-10 de [`docs/errores.md`](../../../docs/errores.md).

## Tests
- `UnitTest/GenAi/AgreementServiceTests.cs`: lo que recibe la IA (sin nombres, teléfonos ni ids), el aviso, sin mensajes, sin proveedor y con la IA caída.
- `UnitTest/GenAi/AgreementExtractionTests.cs`: el filtro de datos personales y el adaptador de Gemini (llave en el header, esquema, 429/500/400, bloqueo, sin red, tiempo agotado y sin llave).
- `IntegrationTest/GenAi/AgreementFlowTests.cs`: sin IA, el chat y los pedidos siguen.
- `SecurityTest/Auth/AgreementsAuthTests.cs`: 401, 404 a terceros, cuenta suspendida, el tope por persona y Gemini apagado sin llave en Production.

## Criterios
- [x] La IA solo se ejecuta cuando un participante lo solicita (R1, RN-39).
- [x] Ambos participantes saben que la conversación se procesó con IA (R2, RN-40). El aviso de la primera vez: `musubi-app:HU-32`.
- [x] Solo se envía la información mínima necesaria (R3, RNF-06).
- [x] Indicador de proceso: `musubi-app:HU-32` (RNF-12).
- [x] Si el servicio de IA no está disponible, se informa y el chat sigue funcionando (R4, RNF-15).
- [x] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- `NOTE (HU-47)`: el aviso de privacidad dice qué sale a Gemini y cuánto se conserva.
- Un resumen de texto libre de la conversación no se hace: la maqueta solo tiene la propuesta del acuerdo.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 10-Nov-2026 (detallado e implementado)
- **Detalle:** Gemini en la capa gratuita, con el chat filtrado, aviso en el chat y degradación a llenar el resumen a mano.
