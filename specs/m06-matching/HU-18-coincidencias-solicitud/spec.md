# HU-18 — Ver coincidencias al publicar una solicitud

**HU / Jira / Sprint / Módulo / Contraparte:** HU-18 / SCRUM-40 / Sprint 3 (26 oct – 6 nov 2026) / M-06 / [musubi-app:HU-18](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m06-matching/HU-18-coincidencias-solicitud)

## Contexto
Jira (SCRUM-40): Como **comprador**, quiero **que al publicar mi solicitud el sistema me muestre productos ya publicados que coinciden**, para **no tener que buscarlos manualmente**.

- Maqueta: `Necesidad-1`, `Necesidad-3`, `Necesidad-publicada`, `Mis-solicitudes`.
- Trazabilidad: RF-22, RF-24, RF-25 · CU-13 · RN-32, RN-33 · RNF-11.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-13`, `HU-06`.

## Requisitos
1. Al publicar una solicitud, el sistema la compara con las publicaciones **Disponibles** y muestra las que coinciden.
2. La coincidencia considera como mínimo: misma categoría, similitud entre nombre o descripción y lo buscado, y precio dentro del presupuesto (RN-33).
3. Si no hay coincidencia exacta, se sugieren productos similares (misma categoría, términos parecidos o precio cercano al presupuesto) (RF-24).
4. Si no hay coincidencias, la solicitud queda visible para recibir ofertas.
5. Cada coincidencia se registra, junto con si se notificó y si terminó en pedido (RF-25).
6. El cálculo se ejecuta en segundo plano (RNF-11).

RNF que aplican: RNF-11.

## Diseño
- Módulo `Musubi.Api/Modules/Matching/` (M-06). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `offers` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Matching/`: reglas de negocio de cada criterio.
- `IntegrationTest/Matching/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Al publicar una solicitud, el sistema la compara con las publicaciones **Disponibles** y muestra las que coinciden.
- [ ] La coincidencia considera como mínimo: misma categoría, similitud entre nombre o descripción y lo buscado, y precio dentro del presupuesto (RN-33).
- [ ] Si no hay coincidencia exacta, se sugieren productos similares (misma categoría, términos parecidos o precio cercano al presupuesto) (RF-24).
- [ ] Si no hay coincidencias, la solicitud queda visible para recibir ofertas.
- [ ] Cada coincidencia se registra, junto con si se notificó y si terminó en pedido (RF-25).
- [ ] El cálculo se ejecuta en segundo plano (RNF-11).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
