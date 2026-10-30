# HU-05 — Consultar perfil y reputación de otro usuario

**HU / Jira / Sprint / Módulo / Contraparte:** HU-05 / SCRUM-34 / Sprint 3 (26 oct – 6 nov 2026) / M-02 / [musubi-app:HU-05](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-05-perfil-publico)

## Contexto
Jira (SCRUM-34): Como **usuario**, quiero **consultar el perfil y la reputación de otro usuario**, para **decidir si hago un trato con él**.

- Maqueta: `Producto`, `Perfil-vendedor`, `Chat-Valeria`.
- Trazabilidad: RF-05, RF-36 · CU-03 · RN-26.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-04`, `HU-27`.

## Requisitos
1. El perfil público muestra nombre, reputación (promedio), número de calificaciones y publicaciones activas (RF-05).
2. Un usuario sin calificaciones se muestra como "Sin calificaciones" (RN-26).
3. El perfil del vendedor es accesible desde el detalle de una publicación (RF-14).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Profiles/` (M-02). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `profiles` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Profiles/`: reglas de negocio de cada criterio.
- `IntegrationTest/Profiles/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] El perfil público muestra nombre, reputación (promedio), número de calificaciones y publicaciones activas (RF-05).
- [ ] Un usuario sin calificaciones se muestra como "Sin calificaciones" (RN-26).
- [ ] El perfil del vendedor es accesible desde el detalle de una publicación (RF-14).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): Se ubica en Sprint 3 porque depende del sistema de reputación.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
