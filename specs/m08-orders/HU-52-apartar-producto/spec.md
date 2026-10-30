# HU-52 — Apartar un producto y verificar la entrega con QR o código

**HU / Jira / Sprint / Módulo / Contraparte:** HU-52 / SCRUM-74 / por asignar / M-08 / [musubi-app:HU-52](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m08-orders/HU-52-apartar-producto)

## Contexto
Jira (SCRUM-74): Como **comprador**, quiero **apartar un producto por 24 horas y recibir un QR o un código**, para **que el vendedor me lo guarde y verifique que soy yo al entregarlo**.

- Maqueta: `Apartar`, `Apartado-listo`, `Mis-apartados`, `Verificar-comprador`, `Apartado-verificado`.
- Trazabilidad: sin RF/RNF en Jira (historia que nació de la maqueta, SCRUM-75).
- Origen: maqueta (sección 4, G-05).

**Depende de:** `02-01`, `02-03`, `HU-11`, `HU-44`.

## Requisitos
1. Al apartar se eligen la fecha, la hora y el punto de entrega (relación con HU-44); el apartado vence a las 24 horas.
2. El comprador recibe un QR y un código de 6 dígitos de un solo uso.
3. El vendedor escanea el QR o escribe el código; solo el vendedor de ese apartado puede verificarlo (RNF-03).
4. "Mis apartados" muestra lo que yo aparté y lo que me apartaron.
5. El código no se puede adivinar por fuerza bruta (intentos limitados).

RNF que aplican: RNF-03.

## Diseño
- Módulo `Musubi.Api/Modules/Orders/` (M-08). Si es la primera HU del módulo, aquí se crea su carpeta (`Modules/README.md`).
- Colecciones: `reservations` (diseño 📐 en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md); el script `db/NNN-*.js` se escribe al implementar).
- Endpoints: se definen al detallar. Cada ruta tiene su fila en `docs/endpoints.md` (`check_endpoints`) y su auth explícita.

## Contratos
Por definir al detallar: filas nuevas en [`docs/endpoints.md`](../../../docs/endpoints.md), errores en [`docs/errores.md`](../../../docs/errores.md) y fixtures en `ContractTest/Fixtures/`.

## Tests
- `UnitTest/Orders/`: reglas de negocio de cada criterio.
- `IntegrationTest/Orders/`: un flujo por criterio de aceptación, con su código de error.
- `SecurityTest/Auth/`: 401 sin sesión y, cuando aplique, solo el dueño actúa (RNF-03, OWASP API1).
- `ContractTest/`: forma JSON de las respuestas nuevas.
- `TestShared/` + `DatabaseTest/`: la batería del repositorio en memoria y en MongoDB real; índices del script.

## Criterios
- [ ] Al apartar se eligen la fecha, la hora y el punto de entrega (relación con HU-44); el apartado vence a las 24 horas.
- [ ] El comprador recibe un QR y un código de 6 dígitos de un solo uso.
- [ ] El vendedor escanea el QR o escribe el código; solo el vendedor de ese apartado puede verificarlo (RNF-03).
- [ ] "Mis apartados" muestra lo que yo aparté y lo que me apartaron.
- [ ] El código no se puede adivinar por fuerza bruta (intentos limitados).
- [ ] Build Release 0/0 · Unit, Integration, Security y Contract al 100 % · `check_endpoints` OK · critic PASS.

## Límites
- Decisión pendiente (Jira): cuántos apartados activos puede tener un comprador a la vez.
- Spec ligera: el diseño, los contratos y las pruebas exactas se fijan al iniciar la HU (`plan.md`).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador (spec ligera)
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** generada desde Jira, la maqueta y el modelo de datos para tener todo el backlog especificado (SCRUM-86, pedido del docente). Al iniciar la HU se completan `plan.md` y `task.md` y se resuelven las decisiones pendientes.
