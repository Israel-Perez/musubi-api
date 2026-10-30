# HU-42 — Consultar indicadores de uso

**HU / Jira / Sprint / Módulo / Contraparte:** HU-42 / SCRUM-65 / Versión posterior (fuera del MVP) / M-14 / [musubi-app:HU-42](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m14-admin/HU-42-indicadores-uso)

## Contexto
Jira (SCRUM-65): Como **administrador**, quiero **consultar indicadores de uso**, para **evaluar cómo funciona la plataforma**.

- Maqueta: sin pantalla propia. Se diseña con la guía de estilo de la maqueta; el lineamiento 4 permite agregar, nunca quitar.
- Trazabilidad: RF-57 · CU-27.
- Origen: Propuesta inicial §11.

**Depende de:** `02-01`, `02-03`, `HU-39`.

## Requisitos
1. El administrador consulta desde un panel los indicadores definidos en el apartado 19 de la propuesta (adopción, oferta, demanda, conexión, operaciones, chat, IA, vendedores y confianza).

RNF que aplican: se confirman al detallar.

## Diseño
- Módulo `Musubi.Api/Modules/Admin/` (M-14). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Sin colección propia prevista en `docs/modelo-datos.md`; se confirma al detallar.
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Admin/`: reglas de negocio de cada criterio.
- `IntegrationTest/Admin/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.

## Criterios
- [ ] El administrador consulta desde un panel los indicadores definidos en el apartado 19 de la propuesta (adopción, oferta, demanda, conexión, operaciones, chat, IA, vendedores y confianza).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Versión posterior al MVP (Jira: `version-posterior`): no entra en la evaluación del 02-Dic-2026.
- Decisión pendiente (Jira): Marcada como versión posterior; no forma parte del MVP.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera — posterior al MVP)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
