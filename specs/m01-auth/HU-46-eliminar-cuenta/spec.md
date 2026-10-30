# HU-46 — Eliminar mi cuenta y mis datos

**HU / Jira / Sprint / Módulo / Contraparte:** HU-46 / SCRUM-32 / Sprint 2 (12 – 23 oct 2026) / M-01 / [musubi-app:HU-46](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m01-auth/HU-46-eliminar-cuenta)

## Contexto
Jira (SCRUM-32): Como **usuario**, quiero **eliminar mi cuenta y mis datos desde la aplicación o desde una página web**, para **tener control sobre mi información personal**.

- Maqueta: `Privacidad`, `Aviso-privacidad`.
- Trazabilidad: RNF-08 · Entregable 1 §12 (requisito de Google Play) · LFPDPPP 2025.
- Origen: Entregable 1 §12 — historia nueva, no estaba en la lista original.

**Depende de:** `02-01`, `02-03`, `HU-01`, `HU-04`.

## Requisitos
1. Existe la opción "Eliminar cuenta" dentro de la aplicación.
2. Existe una página web pública, en el dominio del proyecto, para solicitar la eliminación de la cuenta.
3. Se informa al usuario qué datos se eliminan y cuáles se conservan, y por cuánto tiempo.
4. El procedimiento es congruente con el aviso de privacidad.

RNF que aplican: RNF-08.

## Diseño
- Módulo `Musubi.Api/Modules/Auth/` (M-01). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `users` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Auth/`: reglas de negocio de cada criterio.
- `IntegrationTest/Auth/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Existe la opción "Eliminar cuenta" dentro de la aplicación.
- [ ] Existe una página web pública, en el dominio del proyecto, para solicitar la eliminación de la cuenta.
- [ ] Se informa al usuario qué datos se eliminan y cuáles se conservan, y por cuánto tiempo.
- [ ] El procedimiento es congruente con el aviso de privacidad.
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): Debe estar lista antes de la prueba cerrada en Google Play (inicio 9 nov 2026).
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
