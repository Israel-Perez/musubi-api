# HU-42 — Consultar indicadores de uso

**HU / Jira / Sprint / Módulo / Contraparte:** HU-42 / SCRUM-65 / Sprint 5 (14 nov – 1 dic 2026; antes "versión posterior") / M-14 / [musubi-app:HU-42](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-42-indicadores-uso)

## Contexto
Jira (SCRUM-65): Como **administrador**, quiero **consultar indicadores de uso**, para **evaluar cómo funciona la plataforma**.

- Maqueta: sin pantalla propia; se agrega con la guía de estilo (lineamiento 4). Trazabilidad: RF-57 · CU-27 · apartado 19 de la propuesta (Confluence "Métricas e indicadores").
- **Decisiones (25-Nov-2026, para que la usuaria las revise):**
  - **Usuarios activos** = cuentas que empezaron o renovaron su sesión en el periodo. La app renueva la sesión al usarse, y así no hace falta registrar cada interacción.
  - **Conversión de solicitudes** = compras completadas que nacieron de una oferta ÷ solicitudes publicadas en el periodo. Es una aproximación: un pedido no guarda de qué solicitud salió, solo de qué oferta.
  - **Tiempo para encontrar un producto** = horas hasta la primera oferta y hasta aceptar una. "Hasta el pedido" no se puede medir por la misma razón.
  - **IA:** se agrega `aiUsage` para contar los resúmenes y los acuerdos confirmados sin correcciones, porque el acuerdo se borra al crear el pedido (RN-44).
  - **Periodos:** la última semana o el último mes (`week`/`month`), como en la propuesta.

**Depende de:** `HU-39/40/41` (rol y `AdminGate`) y los datos de cada módulo.

## Requisitos
1. **R1 · Las 9 áreas del apartado 19**, en ese orden, con estas métricas:

   | Área | Métricas |
   |---|---|
   | Adopción | Usuarios registrados (validados y nuevos) y usuarios activos |
   | Oferta | Publicaciones creadas por categoría y activas |
   | Demanda | Solicitudes por categoría, ofertas por solicitud y % de ofertas directas |
   | Conexión | Coincidencias y cuántas terminaron en pedido, conversión de solicitudes y búsquedas guardadas atendidas |
   | Operaciones | Compras completadas y productos vendidos, y tiempo para encontrar un producto |
   | Chat | Conversaciones iniciadas y % con pedido |
   | IA | Resúmenes y % confirmados sin correcciones |
   | Vendedores | Avisados por "sin venderse" y vendidos después |
   | Confianza | Calificación promedio y reportes procedentes |

2. **R2 · Solo el administrador** activo con el rol en la base (`AdminGate`) y solo su campus. Un estudiante, una cuenta suspendida o un token viejo reciben 403.
3. **R3 · Periodo:** `week` (por defecto) o `month`, contado hasta este instante; otro valor da 400 `admin.invalid_period`.
4. **R4 · Sin personas (RNF-06):** solo conteos y promedios; ningún nombre, correo ni id.
5. **R5 · Cada módulo calcula lo suyo** detrás de su contrato (Modules/README); Administración solo junta y cruza.
6. **R6 · Sin base no hay porcentaje:** `value: null`, no 0.

## Diseño
- `Modules/Admin/UsageIndicatorsService.cs`, `AdminIndicatorsController.cs` y los DTO `UsageIndicatorsResponse`, `IndicatorArea`, `Indicator` e `IndicatorBreakdown`.
- **Contratos (uno por módulo):**

  | Módulo | Contrato | Repositorio |
  |---|---|---|
  | Auth | `IUserIndicators` | `IUserRepository.UsageAsync`, `IRefreshTokenRepository.UsersSignedInAsync` |
  | Listings | `IListingIndicators` | `IProductRepository.UsageAsync` |
  | PurchaseRequests | `IWantedIndicators` | `IWantedRepository.CreatedBetweenAsync`, `IOfferRepository.FindByRequestsAsync` y `UsageAsync` |
  | Matching | `IMatchIndicators` | `IMatchRepository.UsageAsync` |
  | Orders | `IOrderIndicators` | `IOrderRepository.UsageAsync`, `CountConversationsWithOrdersAsync` y `CompletedForProductsAsync` |
  | Chat | `IChatIndicators` | `IConversationRepository.StartedBetweenAsync` |
  | GenAi | `IAiIndicators` | `IAiUsageRepository` (nuevo) |
  | Notifications | `INotificationIndicators` | `INotificationRepository.FindSentAsync` |
  | Reputation | `ITrustIndicators` | `IRatingRepository.UsageAsync`, `IReportRepository.UsageAsync` |

- `AgreementService` anota en `aiUsage` cada "Resumir acuerdo" y cada confirmación de los dos (con `corrected` si la revisión es mayor que 1).
- `db/036-indicadores.js`: `aiUsage` (validador, `ix_community_at` y TTL de un año) y `notifications.ix_type_created`. Esquema 36.

## Contratos
- Fila `GET /api/v1/admin/indicators` de [`docs/endpoints.md`](../../../docs/endpoints.md).
- Error `admin.invalid_period`.
- Fixture `admin-indicators.json`.
- Colección `aiUsage` en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).

## Tests
- `UnitTest/Admin/UsageIndicatorsServiceTests.cs`:
  - las 9 áreas en orden con sus valores;
  - los cruces (búsquedas de personas del campus, vendidos después del aviso);
  - sin datos, porcentajes `null`;
  - solo el administrador activo y un periodo válido;
  - el redondeo.
- `UnitTest/GenAi/AgreementServiceTests.cs`: cada resumen y cada confirmación quedan anotados, con y sin correcciones.
- Contratos de cada repositorio (`TestShared/`, en memoria y MongoDB):
  - `AuthRepositoryContract` (cuentas y sesiones), `ListingRepositoryContract`, `WantedRepositoryContract` y `OfferRepositoryContract`;
  - `MatchRepositoryContract`, `OrderRepositoryContract`, `ChatRepositoryContract` y `NotificationRepositoryContract`;
  - `RatingRepositoryContract`, `ReputationRepositoryContract` y `AiUsageRepositoryContract`.
- `IntegrationTest/Admin/IndicatorsFlowTests.cs`: una estudiante publica y pide, y el administrador lo ve; ella recibe 403.
- `SecurityTest/Auth/AdminAuthTests.cs`: 401, 403 (estudiante, sin el rol en la base), 400 y que la respuesta no trae correos ni nombres.
- `ContractTest/AdminContractTests.cs` (`admin-indicators.json`) y `DatabaseTest/SchemaScriptTests.cs` (`db/036`).

## Criterios
- [x] El administrador consulta desde un panel los indicadores del apartado 19 (adopción, oferta, demanda, conexión, operaciones, chat, IA, vendedores y confianza) → `TheNineAreasOfTheProposal_InOrder_WithTheirValues`, `TheAdmin_SeesWhatHappensOnTheCampus_AndNobodyElse` y el panel en musubi-app.
- [x] Solo el administrador, sin datos de personas → `Indicators_ForTheAdmin_AreAggregates_WithoutPeople_AndOnlyAKnownPeriod`, `AdminAuthTests` (401/403).
- [x] Build Release 0/0 · `check_endpoints` OK (132) · `check_endpoint_tests` OK · critic PASS.

## Límites
- Los tratos que se cierran fuera de la app no se cuentan: las ventas son un mínimo (lo dice la propuesta).
- La conversión de solicitudes y el tiempo hasta el pedido son aproximaciones (ver Decisiones).
- Los avisos se guardan 60 días: el mes completo sí cabe.
- El uso de la IA solo se cuenta desde esta versión (`aiUsage` empieza vacía).
- Las métricas "para versiones posteriores" de la propuesta (apertura de avisos, retención, satisfacción…) no entran.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (spec ligera) · 25-Nov-2026 (detallada e implementada)
