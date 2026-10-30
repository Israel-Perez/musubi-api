# HU-11 — Ver el detalle de un producto

**HU / Jira / Sprint / Módulo / Contraparte:** HU-11 / SCRUM-28 / Sprint 2 (12 – 23 oct 2026) / M-04 / [musubi-app:HU-11](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-11-detalle-producto)

## Contexto
Jira (SCRUM-28): Como **comprador**, quiero **ver el detalle de un producto**, para **decidir si me interesa antes de contactar al vendedor**.

- Maqueta: `Inicio-visitante`, `Se-busca-visitante`, `Producto-visitante`, `Visitante-modal`, `Producto`, `Perfil-vendedor`, `Chat-Valeria`.
- Trazabilidad: RF-14 · CU-06 · RNF-19.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-06`.

## Requisitos
1. El detalle muestra fotografías, descripción, precio, estado, cantidad, datos del vendedor y su reputación.
2. Desde el detalle se puede contactar al vendedor (chat) o comprar.
3. La interfaz distingue visualmente los productos publicados de las solicitudes de compra (RNF-19).

RNF que aplican: RNF-19.

## Diseño
- Módulo `Musubi.Api/Modules/Marketplace/` (M-04). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `products` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Marketplace/`: reglas de negocio de cada criterio.
- `IntegrationTest/Marketplace/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El detalle muestra fotografías, descripción, precio, estado, cantidad, datos del vendedor y su reputación.
- [ ] Desde el detalle se puede contactar al vendedor (chat) o comprar.
- [ ] La interfaz distingue visualmente los productos publicados de las solicitudes de compra (RNF-19).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
