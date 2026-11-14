# HU-35 — Ver productos recomendados

**HU / Jira / Sprint / Módulo / Contraparte:** HU-35 / SCRUM-58 / Sprint 5 (14 nov – 1 dic 2026) / M-11 / [musubi-app:HU-35](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m11-recommendations/HU-35-productos-recomendados)

## Contexto
Jira (SCRUM-58): Como **comprador**, quiero **ver productos recomendados según mis intereses**, para **descubrir artículos que me pueden servir**.

- Maqueta: `Inicio` (título "Recomendado para ti" de la guía de estilo) y `Producto` / `Producto-visitante` ("Ver parecidos").
- Trazabilidad: RF-48, RF-49 · CU-21 · RN-45, RN-46 · RNF-07 · lineamiento **L-16i** (al menos una función de ML).
- Decisiones de la usuaria (13-Nov-2026):
  - en `Inicio` ("Recomendado para ti") y en `Producto` ("Ver parecidos");
  - señales: ver el detalle, guardar en favoritos, escribirle al vendedor y comprar (sin textos de búsqueda);
  - el historial se guarda 90 días.
- 13-Nov-2026: se cambia "recomendaciones basadas en reglas" por un **modelo de ML** real (L-16i).

**Depende de:** `HU-11` (detalle), `HU-20` (chat), `HU-24` (pedido completado), `HU-53` (favoritos) y `HU-57` (bloqueos).

## Requisitos
1. **R1 · Señales (RF-48):** se registran ver el detalle de algo ajeno, guardar en favoritos, escribirle al vendedor desde la publicación y comprar (pedido completado). Una por persona, publicación y tipo; repetirla actualiza la fecha y el conteo. Nunca falla hacia quien la da (RNF-15).
2. **R2 · Modelo de ML:** ML.NET aprende TF-IDF del catálogo disponible del campus; "Para ti" compara el perfil de la persona (pesos compra 5, chat 3, favorito 2, ver 1; olvido de 30 días) y, con datos suficientes, lo mezcla con factorización de matrices de una clase (filtrado colaborativo).
3. **R3 · Sin datos (RN-46):** sin historial, lo más activo del campus (14 días) y lo más nuevo; lo que falte de una lista personal se completa así.
4. **R4 · Exclusiones (RN-45):** nunca lo propio, lo que ya vio, lo que no está disponible ni lo de personas bloqueadas (HU-57).
5. **R5 · "Ver parecidos":** vecinos más cercanos por texto, con categoría y precio; misma visibilidad que el detalle; sin sesión, sin el vendedor (RNF-05).
6. **R6 · Privacidad (RNF-07):** el historial nunca se expone, vence a los 90 días (TTL) y se borra con la cuenta (HU-46).
7. **R7 · Evidencia:** evaluación leave-one-out (hit@K contra el azar) en las pruebas y en los logs de cada entrenamiento. La misma evaluación decide si el filtrado colaborativo entra: solo si mejora al contenido (fix del 14-Nov-2026).
8. **R8 · Se puede rechazar:** el aviso de privacidad (HU-47) la declara finalidad adicional que se rechaza desde `Ajustes`. Con `recommendations: false` en las preferencias no se registra nada, el historial se borra al momento y "Para ti" es solo general.

## Diseño
- `Modules/Recommendations/`: `InteractionRecorder` (`IInteractionRecorder`, `IAccountDataEraser`), `RecommendationModel` (+ `SparseVector`, `CollaborativeScorer`), `RecommendationModelStore` (un modelo por campus, 30 minutos), `RecommendationService`, `RecommendationsController`.
- M-02: `IRecommendationConsent` (lee `preferences.recommendations`) y `PUT /profiles/me/preferences` con `recommendations?`; M-11 le da `IInteractionHistory.ForgetAsync`.
- M-03: `IRecommendationCatalog` (`AvailableAsync`, `FindVisibleAsync` con las reglas del detalle, `CardsAsync`) e `IProductRepository.FindAvailableAsync`.
- Datos: `interactions` (`db/022-interacciones.js`); esquema 22.
- Paquetes: `Microsoft.ML` 5.0.1 y `Microsoft.ML.Recommender` 0.23.1 (MIT; constitución §6). `Newtonsoft.Json` entra al Package Source Mapping.

## Contratos
- Filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md) (sección M-11): `GET /api/v1/recommendations` y `GET /api/v1/products/{id}/similar`. Cada item tiene la forma de `ProductResponse`.
- Fixture `ContractTest/Fixtures/recommendation-page.json`.

## Tests
- `UnitTest/Recommendations/RecommendationModelTests.cs`: parecidos del mismo tipo (y nunca el mismo), plurales y acentos por trigramas, exclusiones, el gusto aprendido, pesos y olvido, sin historial, lo general, filtrado colaborativo con datos suficientes, **leave-one-out ≥ 0.8 y al menos el doble del azar**, catálogo vacío.
- `UnitTest/Recommendations/RecommendationServiceTests.cs`: personal y general, completa con lo general, exclusiones, límite de 1 a 20, parecidos con y sin sesión y 404, y el reentrenamiento a los 30 minutos.
- `UnitTest/Recommendations/InteractionRecorderTests.cs`: 90 días, conteo, ids inválidos, borrado con la cuenta y pesos.
- `UnitTest/Orders/OrderLifecycleTests.cs`: completar registra la compra una vez.
- `TestShared`: `InteractionRepositoryContract` y `Available_*` (publicaciones), en memoria y en MongoDB real.
- `IntegrationTest/Recommendations/RecommendationFlowTests.cs`, `SecurityTest/Auth/RecommendationsAuthTests.cs` y `ContractTest/ListingsContractTests.cs`.

## Criterios
- [x] Se registran productos consultados, favoritos, chats y compras (RF-48) → `InteractionRecorderTests`, `OrderLifecycleTests` e integración.
- [x] Las recomendaciones salen de un modelo de ML (L-16i) → `RecommendationModelTests` (leave-one-out).
- [x] Si no hay datos suficientes, se muestran recomendaciones generales (RN-46) → `ForYou_WithoutHistory_IsGeneral_*`.
- [x] No se recomiendan productos propios, vendidos o inactivos, ni de personas bloqueadas (RN-45) → `ForYou_NeverOwnItems_*` e integración.
- [x] El historial de interacciones es privado (RNF-07) → no hay ruta que lo exponga; TTL de 90 días y borrado con la cuenta.
- [x] Se puede rechazar desde `Ajustes` (aviso de privacidad) → `WhoRejectsTheRecommendations_*`, `ForYou_IfTheyRejectedIt_IsOnlyGeneral` e integración.
- [x] Build Release 0/0 · Unit 669 · Integration 174 · Security 226 · Contract 22 · `check_endpoints` OK (88) · critic PASS.

## Límites
- Cada instancia entrena su propio modelo (en memoria); las dos pueden diferir unos minutos.
- Con pocos datos el filtrado colaborativo no se activa: el modelo usa solo el texto. La métrica real del piloto se lee en los logs de cada entrenamiento.
- Las búsquedas no son señal (decisión de la usuaria).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador ligero) · 14-Nov-2026 (detallado e implementado)
- **Detalle:** con las decisiones de la usuaria del 13-Nov-2026; ML real en lugar de reglas.
