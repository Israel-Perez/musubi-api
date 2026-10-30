# HU-12 — Guardar una búsqueda sin resultados

**HU / Jira / Sprint / Módulo / Contraparte:** HU-12 / SCRUM-51 / Sprint 4 (9 – 20 nov 2026) / M-04 / [musubi-app:HU-12](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m04-marketplace/HU-12-guardar-busqueda)

## Contexto
Jira (SCRUM-51): Como **comprador**, quiero **que se conserve una búsqueda que no tuvo resultados**, para **recibir un aviso cuando aparezca un producto compatible**.

- Maqueta: `Busqueda`, `Busqueda-filtros`, `Busqueda-vacia`.
- Trazabilidad: RF-15, RF-23, RF-38 · CU-07 · Flujo D · RN-34 · RNF-07.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-09`, `HU-29`.

## Requisitos
1. Cuando una búsqueda no tiene resultados, el sistema ofrece **guardar la búsqueda** (interés privado) o **convertirla en solicitud** (necesidad pública).
2. El usuario puede consultar y eliminar sus búsquedas guardadas.
3. Cuando se publica un producto compatible, el usuario recibe: "El producto que buscaste anteriormente ya está disponible."
4. Las búsquedas guardadas son privadas (RNF-07) y expiran después de [POR DEFINIR] días (RN-34).

RNF que aplican: RNF-07.

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
- [ ] Cuando una búsqueda no tiene resultados, el sistema ofrece **guardar la búsqueda** (interés privado) o **convertirla en solicitud** (necesidad pública).
- [ ] El usuario puede consultar y eliminar sus búsquedas guardadas.
- [ ] Cuando se publica un producto compatible, el usuario recibe: "El producto que buscaste anteriormente ya está disponible."
- [ ] Las búsquedas guardadas son privadas (RNF-07) y expiran después de [POR DEFINIR] días (RN-34).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
