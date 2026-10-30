# HU-51 — Comparar opciones y pedir aviso de disponibilidad

**HU / Jira / Sprint / Módulo / Contraparte:** HU-51 / SCRUM-70 / por asignar / M-04 / [musubi-app:HU-51](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-51-comparar-avisame)

## Contexto
Jira (SCRUM-70): Como **comprador**, quiero **comparar un producto con publicaciones similares y pedir un aviso cuando vuelva a estar disponible**, para **elegir la mejor opción y no perder un producto agotado**.

- Maqueta: `Producto`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (sección 3, G-04).

**Depende de:** `02-01`, `02-03`, `HU-11`, `HU-29`.

## Requisitos
1. El detalle muestra publicaciones similares comparadas por reputación del vendedor, ventas y precio.
2. En un producto agotado, el botón "Avísame" registra el interés.
3. Cuando el producto vuelve a tener existencias, llega una notificación push (relación con HU-29 y HU-31).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Marketplace/` (M-04). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `alerts` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
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
- [ ] El detalle muestra publicaciones similares comparadas por reputación del vendedor, ventas y precio.
- [ ] En un producto agotado, el botón "Avísame" registra el interés.
- [ ] Cuando el producto vuelve a tener existencias, llega una notificación push (relación con HU-29 y HU-31).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): el criterio de "similar" (misma categoría y palabras clave) y si se apoya en el modelo de ML de HU-35.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
