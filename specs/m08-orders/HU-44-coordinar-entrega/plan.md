# plan.md — HU-44 Coordinar la entrega en un punto del campus

## 1. Componentes
Ninguno nuevo. Lo cubren `OrderService` y `MeetingPointDirectory` (HU-23), `OrderLifecycleService` (HU-24) y `ReservationService` (HU-52).

## 2. Decisiones (con su porqué)
- **No se duplica código:** HU-23 ya pide el punto de la lista y la fecha dentro de la ventana del campus; HU-52 lo mismo para apartar. HU-44 se cierra con esa evidencia.
- **Quién administra la lista (propuesta de Jira, 16-Nov-2026):** el administrador del campus, con HU-41. Mientras tanto la siembra `db/014`.

## 3. Excepciones al stack
Ninguna.

## 4. Guardarraíles
El punto se valida contra la lista del campus de quien compra (`orders.invalid_meeting_point`) y la hora contra la ventana (`orders.invalid_delivery_time`).

## 5. Validación
Las pruebas de HU-23, HU-24 y HU-52 listadas en `spec.md`.

## 6. Secuencia
Conciliar la spec, `task.md` y `docs/trazabilidad.md`.
