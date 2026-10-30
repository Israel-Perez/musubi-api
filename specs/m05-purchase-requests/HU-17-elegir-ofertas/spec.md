# HU-17 — Ver y elegir ofertas recibidas

**HU / Jira / Sprint / Módulo / Contraparte:** HU-17 / SCRUM-39 / Sprint 3 (26 oct – 6 nov 2026) / M-05 / [musubi-app:HU-17](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m05-purchase-requests/HU-17-elegir-ofertas)

## Contexto
Jira (SCRUM-39): Como **comprador**, quiero **ver las ofertas que recibí en mi solicitud**, para **comparar opciones y elegir la que más me convenga**.

- Maqueta: `Necesidad-1`, `Necesidad-3`, `Necesidad-publicada`, `Mis-solicitudes`.
- Trazabilidad: RF-21 · CU-12.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-16`.

## Requisitos
1. El comprador ve las ofertas recibidas en cada solicitud.
2. Desde cada oferta puede abrir un chat con quien ofrece, rechazarla o aceptarla.
3. Aceptar una oferta inicia un pedido.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/PurchaseRequests/` (M-05). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `offers` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/PurchaseRequests/`: reglas de negocio de cada criterio.
- `IntegrationTest/PurchaseRequests/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El comprador ve las ofertas recibidas en cada solicitud.
- [ ] Desde cada oferta puede abrir un chat con quien ofrece, rechazarla o aceptarla.
- [ ] Aceptar una oferta inicia un pedido.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
