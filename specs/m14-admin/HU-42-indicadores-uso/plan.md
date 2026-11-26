# plan.md — HU-42 Consultar indicadores de uso

## 1. Componentes
- M-14: `UsageIndicatorsService`, `AdminIndicatorsController`, los DTO y `AdminErrors.InvalidPeriod`.
- En cada módulo, su `I…Indicators` (contrato + implementación) y un método por repositorio (Mongo y en memoria).
- M-10: `IAiUsageRepository` (`aiUsage`) y las dos anotaciones en `AgreementService`.
- `db/036-indicadores.js`, `MongoSchema.ExpectedVersion = 36`.

## 2. Decisiones (con su porqué)
- **Un contrato por módulo y no una "bolsa" genérica:** algunos indicadores cruzan módulos (conversaciones con pedido, avisos con ventas). Con contratos explícitos el cruce queda en un solo lugar, el servicio, y cada módulo sigue siendo dueño de sus colecciones.
- **Conteos en la base y no en la app:** `CountDocuments`, `Distinct` y una agrupación por categoría. Lo que se lee completo (solicitudes, ofertas de esas solicitudes, pedidos completados) está acotado al periodo y a un tope, y un campus queda muy por debajo.
- **`aiUsage` en lugar de conservar el acuerdo:** RN-44 lo borra al crear el pedido. Anotar solo el campus, el tipo y si se corrigió mide la IA sin guardar conversaciones.
- **Usuarios activos por sesión:** el `refreshToken` nace cada vez que la app renueva la sesión, así que mide uso real sin una colección de eventos.
- **Periodo hasta "ahora" incluido:** los módulos cuentan `[from, to)` y el servicio pasa `ahora + 1 tick`.
- **Porcentaje sin base = `null`:** un 0 % diría que nadie convirtió cuando en realidad no hubo solicitudes.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
- RNF-03: `[Authorize(Roles = admin)]` + `AdminGate` (la base manda).
- RNF-06: la respuesta solo trae conteos; lo comprueba `SecurityTest`.
- Modules/README: ningún módulo lee colecciones ajenas.

## 5. Validación
Unit, Integration, Security, Contract y DatabaseTest (CI); `check_endpoints` y `check_endpoint_tests`.

## 6. Secuencia
T1 → T2 → T3 → T4 → T5.
