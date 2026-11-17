# plan.md — HU-40 Suspender y reactivar cuentas

Se implementa con HU-39, en el mismo módulo y PR. Los componentes, las decisiones, los guardarraíles, la validación y la secuencia están en [`../HU-39-moderar-contenido/plan.md`](../HU-39-moderar-contenido/plan.md).

Decisiones propias de HU-40:

- **Suspender cierra todas las sesiones** (`RevokeAllForUserAsync`, como al cambiar la contraseña). El inicio de sesión y la renovación ya rechazaban `suspended` desde HU-02.
- **Pausar y no retirar sus publicaciones.** `hidden` es reversible por su dueño desde "Mis productos". `removed` es solo para lo que un reporte procedente retira (HU-39).
- **No se reactivan solas.** Al reactivar la cuenta, su dueño decide qué vuelve a la vitrina. Así no regresa algo que motivó la suspensión.
- **RN-27 como sugerencia**, con 3 procedentes. Ver la decisión en el plan de HU-39.
