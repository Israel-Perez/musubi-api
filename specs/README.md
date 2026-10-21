# Specs de musubi-api — roadmap SDD

Cada cambio nace de una spec que sigue [`_template.md`](_template.md) (gobernanza en `00-02`). Una spec es una carpeta con `spec.md`, `plan.md` y `task.md`.

<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
## Convención de IDs y carpetas

- **Specs técnicas:** `NN-NN` en `specs/phase-NN-<nombre>/NN-NN-<slug>/`.
- **Specs funcionales:** `HU-NN` en `specs/mNN-<modulo>/HU-NN-<slug>/`, con **el mismo ID y la misma carpeta en los dos repos**. Cada una describe su lado y declara su `**Contraparte:**`.
- Las specs de cada HU se crean **al iniciar el sprint** de esa HU (calendario en la constitución §1 y en `docs/trazabilidad.md`).
- Para citar una spec del otro repo se antepone el repo: `musubi-api:02-01`, `musubi-app:HU-01`. Los `NOTE (…)` usan los mismos IDs.
- Módulos y carpetas: [`docs/modulos.md`](../docs/modulos.md).
<!-- Fin de sección compartida -->

## Roadmap

| Fase | Specs | Estado |
|---|---|---|
| 00 — Constitución | 00-01 Principios y stack · 00-02 Gobernanza SDD y DoD · 00-03 NFR · 00-04 Lecciones aprendidas | 🚧 Borrador con evidencia |
| 01 — Fundación | 01-01 Setup y configuración · 01-02 Pipeline y DI · 01-03 Arranque, health y ping · 01-04 Referencia de configuración · 01-05 Estructura, calidad y CI | 🚧 Borrador con evidencia |
| 02 — Arquitectura y modelo de datos | 02-01 Modelo de documentos MongoDB, índices y script `mongosh` (Testcontainers) · 02-02 Caché en 3 niveles (Redis/Valkey, salida HTTP) y paginación · 02-03 Contrato de errores HTTP + diseño JWT/refresh/logout · 02-04 Rendimiento y endurecimiento HTTP (headers, rate limit, compresión) · 02-05 Despliegue en AWS con Terraform (ALB + 2 EC2 + CloudFront, Atlas, crear/eliminar) · 02-06 Observabilidad (CloudWatch) · 02-07 Moderación de contenido (Rekognition/Comprehend) | Pendiente (se redacta antes de HU-01) |
| Módulos | `m01-auth/` … `m14-admin/` — una carpeta por módulo, una spec por HU | Por sprint |

Los IDs de la fase 02 son **tentativos**: son los dueños de los `NOTE (02-0N)` que ya existen en el código. Si cambian, se actualizan los NOTE con `grep "NOTE (02-"`.

## Estado de las specs

La matriz viva está en [`docs/trazabilidad.md`](../docs/trazabilidad.md).
