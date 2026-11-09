# HU-13 — Publicar una necesidad de compra (plan)

## Decisiones
- **5 activas y 30 días:** la spec los dejaba [POR DEFINIR]. Son conservadores para el piloto y se ajustan con datos (`WantedService.MaxActive` y `DefaultDays`).
- **Vencida se calcula al leer:** `expiresAt` decide el estado efectivo. Así no hace falta un proceso en segundo plano, y `CountActiveAsync` filtra `expiresAt > now`.
- **Día del campus en UTC-6 fijo:** sin depender de `tzdata` en la imagen. Elegir "hoy" vale hasta la medianoche del campus.
- **Copias R8:** el nombre corto de quien publica y el nombre de la categoría se copian, para que la tarjeta "Así la verá tu comunidad" y el tablero (HU-15) no lean otras colecciones.
- **Se modera antes de guardar y después de contar:** si ya tiene 5 activas, no se gasta una moderación.

## Archivos
- **Nuevos:** `Modules/PurchaseRequests/*` y `Modules/Listings/CategoryDirectory.cs`.
- **Script:** `db/009-solicitudes.js`; `MongoSchema.WantedRequests` y `ExpectedVersion = 9`.
- **Pruebas:** `WantedServiceTests`, `WantedRepositoryContract` (en memoria y Mongo), `WantedFlowTests`, `WantedAuthTests`, `WantedContractTests` y `SchemaScriptTests`.
