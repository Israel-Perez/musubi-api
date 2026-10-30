# HU-35 — Ver productos recomendados

**HU / Jira / Sprint / Módulo / Contraparte:** HU-35 / SCRUM-58 / Sprint 5 (23 nov – 4 dic 2026) / M-11 / [musubi-app:HU-35](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m11-recommendations/HU-35-productos-recomendados)

## Contexto
Jira (SCRUM-58): Como **comprador**, quiero **ver productos recomendados según mis intereses**, para **descubrir artículos que me pueden servir**.

- Maqueta: `Inicio`, `Recien-publicados`, `Recien-publicados-antiguas`.
- Trazabilidad: RF-48, RF-49 · CU-21 · RN-45, RN-46 · RNF-07.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-09`, `HU-11`.

## Requisitos
1. Se registran búsquedas, productos consultados, categorías de interés, compras, solicitudes e interacciones (RF-48).
2. Se muestran recomendaciones basadas en reglas: productos recientes de las categorías que el usuario más busca o consulta.
3. Si no hay datos suficientes, se muestran recomendaciones generales (productos recientes o categorías más activas) (RN-46).
4. No se recomiendan productos propios, vendidos o inactivos (RN-45).
5. El historial de interacciones es privado (RNF-07).

RNF que aplican: RNF-07.

## Diseño
- Módulo `Musubi.Api/Modules/Recommendations/` (M-11). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `recommendations` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.
- Modelo de ML detrás de una interfaz; sin datos suficientes se responde vacío, nunca inventado (RNF-15).

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Recommendations/`: reglas de negocio de cada criterio.
- `IntegrationTest/Recommendations/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Se registran búsquedas, productos consultados, categorías de interés, compras, solicitudes e interacciones (RF-48).
- [ ] Se muestran recomendaciones basadas en reglas: productos recientes de las categorías que el usuario más busca o consulta.
- [ ] Si no hay datos suficientes, se muestran recomendaciones generales (productos recientes o categorías más activas) (RN-46).
- [ ] No se recomiendan productos propios, vendidos o inactivos (RN-45).
- [ ] El historial de interacciones es privado (RNF-07).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): Versión básica en el MVP; el modelo con ML.NET queda para una etapa posterior.
- Decisión pendiente (Jira): Recomendación: implementar el registro de interacciones (RF-48) desde Sprint 2–3 para acumular datos.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
