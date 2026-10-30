# HU-16 — Responder "Yo tengo este producto"

**HU / Jira / Sprint / Módulo / Contraparte:** HU-16 / SCRUM-38 / Sprint 3 (26 oct – 6 nov 2026) / M-05 / [musubi-app:HU-16](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-16-yo-tengo-este-producto)

## Contexto
Jira (SCRUM-38): Como **usuario**, quiero **responder a una solicitud con "Yo tengo este producto", ya sea vinculando una publicación o describiendo un producto que todavía no publico**, para **ofrecerlo directamente al comprador**.

- Maqueta: `Se-busca`, `Yo-tengo-vincular`, `Yo-tengo-oferta`, `Yo-tengo-enviada`.
- Trazabilidad: RF-20, RF-40 · CU-11 · Flujo B · RN-06, RN-13, RN-14.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-15`, `HU-06`.

## Requisitos
1. Desde una solicitud **Activa** el usuario elige "Yo tengo este producto".
2. Puede **vincular una publicación existente** o crear una **oferta directa** (descripción, precio, estado del producto y fotografía opcional) sin publicarlo en el marketplace.
3. Si el usuario no tiene publicaciones, solo se ofrece la oferta directa.
4. La oferta directa solo es visible para el comprador que hizo la solicitud (RN-13).
5. Solo se permite una oferta por solicitud, que puede editarse (RN-14); no se puede responder a solicitudes propias (RN-06).
6. Si el precio supera el presupuesto, el sistema lo advierte pero permite enviar la oferta.
7. El comprador recibe una notificación (RF-40).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/PurchaseRequests/` (M-05). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `offers` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Moderación antes de guardar el texto o las imágenes (L-16b): `NOTE (02-07)`.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/PurchaseRequests/`: reglas de negocio de cada criterio.
- `IntegrationTest/PurchaseRequests/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Desde una solicitud **Activa** el usuario elige "Yo tengo este producto".
- [ ] Puede **vincular una publicación existente** o crear una **oferta directa** (descripción, precio, estado del producto y fotografía opcional) sin publicarlo en el marketplace.
- [ ] Si el usuario no tiene publicaciones, solo se ofrece la oferta directa.
- [ ] La oferta directa solo es visible para el comprador que hizo la solicitud (RN-13).
- [ ] Solo se permite una oferta por solicitud, que puede editarse (RN-14); no se puede responder a solicitudes propias (RN-06).
- [ ] Si el precio supera el presupuesto, el sistema lo advierte pero permite enviar la oferta.
- [ ] El comprador recibe una notificación (RF-40).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): decidir si una oferta directa aceptada se convierte en publicación (Anexo A, observación 2).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
