# HU-50 — Manejar la cantidad disponible de un producto

**HU / Jira / Sprint / Módulo / Contraparte:** HU-50 / SCRUM-67 / por asignar / M-03 / [musubi-app:HU-50](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-50-cantidad-disponible)

## Contexto
Jira (SCRUM-67): Como **vendedor**, quiero **indicar cuántas piezas tengo de un producto**, para **vender varias unidades con una sola publicación y que los compradores sepan cuántas quedan**.

- Maqueta: `Vender-4`, `Producto`, `Mis-productos`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (secciones 3, 5 y 10; G-03).

**Depende de:** `02-01`, `02-03`, `HU-06`, `HU-23`.

## Requisitos
1. Al publicar se captura la cantidad disponible (paso "Estado y cantidad").
2. El detalle muestra "Quedan N", "Último" o "Agotado" según el inventario.
3. Un apartado o una venta descuenta la cantidad de forma atómica, sin vender de más.
4. El buscador permite ocultar los agotados.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Listings/` (M-03). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `products` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Listings/`: reglas de negocio de cada criterio.
- `IntegrationTest/Listings/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Al publicar se captura la cantidad disponible (paso "Estado y cantidad").
- [ ] El detalle muestra "Quedan N", "Último" o "Agotado" según el inventario.
- [ ] Un apartado o una venta descuenta la cantidad de forma atómica, sin vender de más.
- [ ] El buscador permite ocultar los agotados.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): si un apartado vencido regresa la unidad al inventario de forma automática (relación con HU-53).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
