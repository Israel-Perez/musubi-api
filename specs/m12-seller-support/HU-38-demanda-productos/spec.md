# HU-38 — Saber si hay demanda para mis productos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-38 / SCRUM-61 / Sprint 5 (23 nov – 4 dic 2026) / M-12 / [musubi-app:HU-38](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m12-seller-support/HU-38-demanda-productos)

## Contexto
Jira (SCRUM-61): Como **vendedor**, quiero **saber si hay usuarios buscando productos similares a los que no he vendido**, para **ofrecérselos directamente**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-53 · CU-23 · RN-37.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-13`, `HU-08`.

## Requisitos
1. Si existe demanda relacionada, se muestra: "Existen usuarios que están buscando productos similares al que tienes publicado."
2. Las solicitudes públicas pueden mostrarse; las búsquedas guardadas solo de forma agregada, sin identificar a nadie (p. ej. "3 usuarios buscan productos similares") (RN-37).
3. Desde ahí el vendedor puede ofrecer su producto en las solicitudes públicas.

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/SellerSupport/` (M-12). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `recommendations` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Modelo de ML detrás de una interfaz; sin datos suficientes se responde vacío, nunca inventado (RNF-15).

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/SellerSupport/`: reglas de negocio de cada criterio.
- `IntegrationTest/SellerSupport/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Si existe demanda relacionada, se muestra: "Existen usuarios que están buscando productos similares al que tienes publicado."
- [ ] Las solicitudes públicas pueden mostrarse; las búsquedas guardadas solo de forma agregada, sin identificar a nadie (p. ej. "3 usuarios buscan productos similares") (RN-37).
- [ ] Desde ahí el vendedor puede ofrecer su producto en las solicitudes públicas.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
