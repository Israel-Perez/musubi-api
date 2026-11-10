# HU-14 — Editar, cerrar o cancelar mi solicitud (plan)

## Decisiones
- **`PUT` con todos los datos, como publicar:** la app ya los tiene (los muestra `Necesidad-1…3`) y no hay que adivinar qué significa un `null`. Un opcional vacío se quita.
- **Con ofertas, el producto y la categoría no cambian:** quien ofreció respondió a ese producto; cambiarlo dejaría ofertas sin sentido. El presupuesto, la cantidad, la fecha, la descripción y las características sí cambian.
- **El vencimiento se recalcula, pero los 30 días cuentan desde que se publicó (RN-10):** editar no alarga una solicitud sin fecha.
- **Cerrar y cancelar son dos rutas:** la maqueta distingue "Cerradas" y los criterios distinguen "ya lo consiguió" de "ya no lo necesita". Las dos son idempotentes para que un doble toque no muestre un error.
- **Una vencida tampoco cambia:** ya salió del tablero; si todavía la necesita, publica otra.
- **Las ofertas pendientes no se tocan al cerrar:** HU-16 y HU-17 ya revisan que la solicitud esté activa. Avisar a quien ofreció es HU-30.
- **Sin leer antes de escribir:** el dueño, el estado y el vencimiento van en el filtro (OWASP API1). Solo si no cambió nada se lee para responder el error correcto.

## Archivos
- **Cambian:**
  - `Modules/PurchaseRequests/{WantedService, WantedController, WantedDtos, WantedErrors}.cs`;
  - `Modules/PurchaseRequests/Data/{WantedRepositories, WantedDocuments}.cs`.
- **Pruebas:** `WantedServiceTests`, `WantedRepositoryContract`, `WantedFlowTests`, `ManageWantedAuthTests` (nuevo), `WantedAuthTests` y `WantedContractTests`.
- **Docs:** `docs/endpoints.md` (57 filas), `docs/errores.md`, `docs/modelo-datos.md`, `docs/versionado.md` (`0.5.0-alpha.13`), `docs/trazabilidad.md`, `AGENTS.md` y `Memoria.md`.
