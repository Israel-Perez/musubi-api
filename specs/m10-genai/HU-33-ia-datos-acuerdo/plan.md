# plan.md — HU-32, HU-33 y HU-34: el acuerdo con IA (API)

## 1. Componentes
- **Nuevo `Modules/GenAi/`:**
  - `Data/AgreementDocuments.cs` (`AgreementDocument`, `AgreementProposal`, `AgreementStatuses`, `AgreementFields`) y `Data/AgreementRepositories.cs` (`IAgreementRepository` en Mongo y en memoria).
  - `AgreementExtraction.cs`: `IAgreementExtractor`, `AgreementPrompt`, `ExtractedAgreement`, `TranscriptRedactor`, `ExtractionJson`, `AgreementPromptText` e `InMemoryAgreementExtractor`.
  - `GeminiAgreementExtractor.cs` (con `GenAiOptions`, `GeminiOptions` y `OffAgreementExtractor`).
  - `AgreementService.cs`, `AgreementsController.cs` (5 rutas), `AgreementsDtos.cs`, `AgreementsErrors.cs`.
  - `GenAiModuleServiceCollectionExtensions.cs`: registro, rate limit `genai`, `IAgreementDirectory` (para Pedidos) y `AgreementAccountEraser` (HU-46).
- **Chat:** `MessageDocument.Kind` (`notice`), `MessageResponse.kind`, evento `agreementChanged` y el contrato `IConversationNotices`.
- **Pedidos:** `CreateOrderRequest.agreementId`, `IOpenOrders.ForConversationAsync`, `IMeetingPointDirectory` y `OrderService.IsValidDeliveryTime`.
- **Datos:** `db/015-acuerdos.js` (`agreements` con `ux_conversation`, `ix_buyer_id`, `ix_seller_id` y `ttl_expires_at`; `messages.kind`), esquema 15.
- **Configuración:** `GenAi:*` y `Gemini:ApiKey` (01-04); en Development, `GenAi:Provider=InMemory`.

## 2. Decisiones
- **Gemini, capa gratuita** (usuaria): costo cero. Choca con L-15c; queda registrado en la bitácora de lineamientos. Como la capa gratuita permite a Google usar lo que recibe, solo sale el chat filtrado (R3 de HU-32) y el aviso de privacidad lo dice (HU-47).
- **`gemini-3.5-flash-lite`:** el modelo estable con más cuota gratuita de los que da la llave (lista medida el 10-Nov-2026). Configurable.
- **Salida JSON con esquema** (`responseSchema`, `enum` con los puntos del campus) y validación propia: el esquema reduce errores, pero la API no confía en él.
- **El aviso sale antes de llamar a la IA:** si el texto salió, los dos lo saben aunque la IA falle (RN-40). Sin llave no hay aviso porque no sale nada.
- **`revision`:** una edición borra las dos confirmaciones y sube la versión; confirmar o editar una versión vieja responde `agreements.changed` (RN-41 sin carreras).
- **El acuerdo se borra al crear el pedido** (usuaria). El pedido toma el precio y el nombre acordados (los dos lo confirmaron); sin acuerdo, el precio sigue siendo el publicado.
- **Un acuerdo por conversación:** pedir otro resumen reemplaza al anterior.
- **El nombre del día** en el texto para la IA se escribe a mano: la imagen corre con `InvariantGlobalization` y no tiene culturas.

## 3. Excepciones al stack
Ninguna: Gemini se llama por REST con `HttpClient` (sin SDK).

## 4. Guardarraíles
- La llave va en el header `x-goog-api-key`, nunca en la URL ni en los logs; la base de la URL es fija (no se configura).
- Solo los participantes ven o tocan el acuerdo (404 a terceros); cuenta suspendida 403; bloqueo HU-57; tope de 16 KB por cuerpo; 6 resúmenes por persona por hora.
- Lo que la IA copia del chat se modera como si lo escribiera una persona.

## 5. Validación
- Unit 496 · Integration 152 · Security 174 · Contract 17.
- Prueba real contra Gemini con un chat de prueba (sin datos de personas): 1.2 s.

## 6. Secuencia
Chat (`kind`, `IConversationNotices`) → datos → extracción y Gemini → servicio y rutas → pedido desde el acuerdo → pruebas → documentación.
