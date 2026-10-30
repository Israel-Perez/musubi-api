# HU-46 — Eliminar mi cuenta y mis datos (tareas)

## T1 — Datos y almacenamiento
- **Qué:** `DeleteAllForUserAsync` en códigos y sesiones, `DeleteBySellerAsync` y `IMediaStorage.DeletePrefixAsync` (S3 y en memoria).
- **Verificar:** `TestShared/` (+3) y `AccountDeletionTests.InMemoryMedia_DeletePrefix_RemovesOnlyThatUser`.
- **Estado:** ✅ Hecho (30-Oct-2026).

## T2 — Servicio y endpoint
- **Qué:** `AccountDeletionService`, `IAccountDataEraser`, `AccountController` y `ListingAccountEraser`.
- **Verificar:** `AccountDeletionTests` (5), `AccountDeletionFlowTests` (3) y `AccountAuthTests` (2).
- **Estado:** ✅ Hecho (30-Oct-2026).

## T3 — Infraestructura y documentación
- **Qué:** `s3:ListBucket` en `iam.tf` (infra `0.3.1`), `docs/endpoints.md` (20 filas), `docs/errores.md`, `docs/versionado.md` (API `0.3.0-alpha.4`), `docs/trazabilidad.md` y `Memoria.md`.
- **Estado:** ✅ Hecho (30-Oct-2026).

## T4 — Despliegue
- **Qué:** `infra-up` y prueba en el celular contra AWS. Requiere la autorización del usuario.
- **Estado:** ⏳ Por hacer.

## Pendientes
- `NOTE (HU-04)`, `NOTE (HU-20)`, `NOTE (HU-23)`: cada módulo agrega su `IAccountDataEraser`.
- `NOTE (HU-40)`: atender desde administración las solicitudes que llegan por correo.
