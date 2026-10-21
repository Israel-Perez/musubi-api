---
name: deferred-scope-fakes
description: Avanzar una HU sin esperar infraestructura futura (JWT, FCM, SignalR, IA, moderación, ML) usando una interfaz con un fake reemplazable y NOTE (ID) trazables. Úsala cuando el alcance completo de una HU depende de otra spec aún no ejecutada.
---

## Propósito
Que ningún slice se bloquee por una dependencia pesada y que ningún pendiente se pierda.

## Cuándo usarla
- La HU necesita un proveedor externo que llega después (IA → HU-32, moderación → 02-07, push → HU-31, chat en tiempo real → HU-20).
- La infraestructura transversal aún no existe (JWT → HU-02, modelo de datos → 02-01).

## Precondiciones
Acuerdo de alcance mínimo con el equipo y spec dueña del reemplazo identificada.

## Pasos
1. **Interfaz reemplazable** en el módulo (`IAgreementSummarizer`, `IPaymentGateway`, `IPushSender`); el slice consume la interfaz.
2. **Fake de producción** (`Fake<Nombre>`) registrado en DI para local y pruebas, con comportamiento determinista; nunca toca la red.
3. Marcar cada diferido con `NOTE (ID)`, donde ID es la spec dueña (`NOTE (HU-32)`, `NOTE (02-01)`). **Nunca `TODO`** (S1135 + critic B1 + Semgrep).
4. Registrar en las 4 capas (skill `traceability`).
5. **Pruebas que fijan el contrato del fake** (p. ej. "publica una vez", "si el proveedor falla, la HU degrada sin 5xx": RNF-15), para que la spec dueña las rompa a propósito al reemplazarlo.
6. **Al llegar a la spec dueña:** `grep -rn "NOTE (ID)"`, cambiar la implementación (el fake se queda para pruebas), volver a correr las suites y quitar los NOTE consumidos de las 4 capas.

## Checklist
Fake detrás de interfaz · `NOTE` con dueño · 0 `TODO` · pruebas del contrato del fake · 4 capas actualizadas.

## Criterios de done
El slice cierra en "🚧 Borrador con evidencia" en su alcance y nada queda pendiente sin dueño.

## Límites/trampas
- Solo se fakea **infraestructura** futura, nunca la lógica de negocio de la HU.
- Los fakes jamás contienen secretos, ni siquiera de prueba que parezcan reales.
- Un fake de IA o de pagos no debe ser seleccionable en Production: gatear el registro por configuración y probarlo.

## Referencias
Skill `traceability`; precedentes en la referencia (fake de TOTP, de hash y de bus de eventos); `00-04` (regla 5).
