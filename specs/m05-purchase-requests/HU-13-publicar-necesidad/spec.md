# HU-13 — Publicar una necesidad de compra

**HU / Jira / Sprint / Módulo / Contraparte:** HU-13 / SCRUM-35 / Sprint 3 (26 oct – 6 nov 2026) / M-05 / [musubi-app:HU-13](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-13-publicar-necesidad)

## Contexto
Jira (SCRUM-35): Como **comprador**, quiero **publicar una solicitud indicando el producto, la descripción, las características, el presupuesto, la cantidad y la fecha en que lo necesito**, para **que otros usuarios sepan lo que busco**.

- Maqueta: `Necesidad-1`, `Necesidad-3`, `Necesidad-publicada`, `Mis-solicitudes`.
- Trazabilidad: RF-16, RF-22 · CU-08 · Flujo B · RN-09, RN-10, RN-12.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-01`.

## Requisitos
1. El formulario captura producto buscado, categoría, descripción, características, presupuesto máximo, cantidad y fecha o periodo.
2. Son obligatorios: producto, categoría, presupuesto máximo y cantidad; descripción, características y fecha son opcionales (RN-09).
3. Si faltan datos obligatorios, el sistema señala los campos pendientes.
4. La solicitud se guarda como **Activa** y queda visible para todos los usuarios.
5. Se respeta el máximo de solicitudes activas por usuario (RN-12; valor [POR DEFINIR]).
6. La solicitud expira en la fecha indicada o, si no hay fecha, tras [POR DEFINIR] días (RN-10).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/PurchaseRequests/` (M-05). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `wantedRequests` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
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
- [ ] El formulario captura producto buscado, categoría, descripción, características, presupuesto máximo, cantidad y fecha o periodo.
- [ ] Son obligatorios: producto, categoría, presupuesto máximo y cantidad; descripción, características y fecha son opcionales (RN-09).
- [ ] Si faltan datos obligatorios, el sistema señala los campos pendientes.
- [ ] La solicitud se guarda como **Activa** y queda visible para todos los usuarios.
- [ ] Se respeta el máximo de solicitudes activas por usuario (RN-12; valor [POR DEFINIR]).
- [ ] La solicitud expira en la fecha indicada o, si no hay fecha, tras [POR DEFINIR] días (RN-10).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
