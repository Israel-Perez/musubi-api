# 02-07 — Moderación de contenido (tareas)

## T1 — Detallar
- **Estado:** ✅ Hecho (03-Nov-2026): Translate + Comprehend para el texto en español y rechazo con 503 si AWS no responde.

## T2 — Plataforma
- **Qué:** `Platform/Moderation` (interfaz, adaptador AWS, moderador en memoria y validación de configuración).
- **Estado:** ✅ Hecho. `ModerationTests` (17).

## T3 — Integración
- **Qué:** fotos de productos y de perfil, y texto de las publicaciones.
- **Estado:** ✅ Hecho. `ModerationFlowTests` (4).

## T4 — Permisos y despliegue
- **Qué:** statement `Moderar` en `infra/stack/iam.tf` (infra `0.3.3`), API `0.4.0-alpha.3` e `infra-up`.
- **Estado:** 🚧 Código listo; falta `infra-up` y la prueba contra AWS.

## Pendientes
- `NOTE (HU-13)`, `NOTE (HU-16)`, `NOTE (HU-20)` y `NOTE (HU-28)`: moderar su texto.
- `NOTE (HU-39)`: revisión humana y `outbox` de pendientes.
