# HU-36 — Ver mis productos con baja rotación

**HU / Jira / Sprint / Módulo / Contraparte:** HU-36 / SCRUM-59 / Sprint 5 (23 nov – 4 dic 2026) / M-12 / [musubi-app:HU-36](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m12-seller-support/HU-36-baja-rotacion)

## Contexto
Jira (SCRUM-59): Como **vendedor**, quiero **saber qué productos llevan mucho tiempo publicados sin venderse**, para **decidir qué hacer con ellos**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-50, RF-51 · CU-22 · Flujo C · RN-35, RN-36.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-08`.

## Requisitos
1. Un proceso periódico detecta publicaciones **Disponibles** con más de [POR DEFINIR] días sin pedidos completados (RN-35, RN-36).
2. El vendedor ve la lista con el aviso: "Estos productos llevan tiempo publicados sin venderse."

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
- [ ] Un proceso periódico detecta publicaciones **Disponibles** con más de [POR DEFINIR] días sin pedidos completados (RN-35, RN-36).
- [ ] El vendedor ve la lista con el aviso: "Estos productos llevan tiempo publicados sin venderse."
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
