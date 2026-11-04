# HU-20 — Iniciar una conversación con el vendedor (tareas)

## T1 — Detallar la spec
- **Qué:** Decisión de tiempo real (SignalR + Redis), endpoints, errores, datos y `plan.md`.
- **Estado:** ✅ Hecho (04-Nov-2026).

## T2 — Datos
- **Crear:** `db/005-chat.js` y `Modules/Chat/Data/*`.
- **Cambiar:** `MongoSchema.ExpectedVersion = 5`; `IProductRepository.FindByIdAsync` (M-03).
- **Verificar:** `ChatRepositoryContract` (en memoria y en Mongo) y `SchemaScriptTests` (+1).
- **Estado:** ✅ Hecho (04-Nov-2026). Contrato en memoria: 6/6. En Mongo corre en el CI (`DatabaseTest`, sin Docker en local).

## T3 — Tiempo real
- **Crear:** `Platform/Realtime` (SignalR, backplane de Redis, usuario por `sub`, token en la query solo en `/hubs`), `ChatHub` y `ChatNotifier`.
- **Verificar:**
  - `ChatFlowTests.Messages_ArriveInRealTime_ToBothParticipants_AndNotToOthers`, con un WebSocket real;
  - `ChatAuthTests`;
  - `ChatServiceTests.Send_WhenTheBackplaneIsDown_StillSavesTheMessage`.
- **Estado:** ✅ Hecho (04-Nov-2026).

## T4 — Servicio y endpoints
- **Crear:** `ChatService`, `ConversationsController`, `ChatErrors`, `ChatDtos` y `ChatAccountEraser`.
- **Verificar:** `ChatServiceTests`, `ChatFlowTests`, `ChatAuthTests` y `ChatContractTests`.
- **Estado:** ✅ Hecho (04-Nov-2026).

## T5 — Documentación
- **Cambiar:** `docs/endpoints.md`, `docs/errores.md`, `docs/modelo-datos.md`, `docs/trazabilidad.md`, `docs/versionado.md`, `01-04` y `Memoria.md`; `check_endpoints` cubre hubs.
- **Estado:** ✅ Hecho (04-Nov-2026).

## T6 — Redis en AWS (usuario)
- **Qué:** Crear la base gratuita de Redis Cloud (us-east-1) y guardar su cadena en `/musubi/prod/Cache/ConnectionString` (SecureString). Luego `infra-up`, que reemplaza las instancias y estas leen el parámetro al arrancar.
- **Verificar:** dos celulares en la misma conversación reciben los mensajes al instante.
- **Estado:** ⏳ Pendiente del usuario.

## Pendientes
- `NOTE (HU-28)`: reportar un mensaje (RN-23).
- `NOTE (HU-21)`: "En línea" según `visibility.online`.
- `NOTE (HU-31)`: aviso push.
- `NOTE (HU-16)`: iniciar desde solicitudes y ofertas.
- La app: `musubi-app:HU-20`.
