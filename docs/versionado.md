# Matriz de versionamiento — MUSUBI

<!-- Sección compartida con musubi-app/musubi-api: si cambia, cámbiala en ambos repos y regístralo en las dos Memoria.md -->
Lineamiento 11 de la materia: la numeración se define **antes del desarrollo** y se aplica **en cada despliegue** (checklist en `musubi-api/docs/bitacora/lineamientos-materia.md`, L-11). Vigente desde el 20-Oct-2026 (SCRUM-66).

## 1. Esquema

Todos los componentes usan **SemVer 2.0** (`MAJOR.MINOR.PATCH[-pre][+build]`):

| Sube | Cuándo (antes de 1.0.0) | Cuándo (desde 1.0.0) |
|---|---|---|
| `MAJOR` | Solo al pasar a `1.0.0` (versión evaluada el 02-Dic-2026) | Cambio incompatible del contrato HTTP, del esquema de datos o de la app mínima soportada |
| `MINOR` | Cierre de sprint con HU nuevas, o un cambio incompatible (en 0.x se permite) | Funcionalidad nueva compatible |
| `PATCH` | Corrección sin funcionalidad nueva (`fix/SCRUM-NN-*`) | Igual |
| `-pre` | `-alpha.N` (interno), `-beta.N` (prueba cerrada de Google Play), `-rc.N` (candidata a la evaluación) | Igual |

Reglas:

- Nada se despliega dos veces con el mismo número. Un despliegue sin cambio de versión es un error del pipeline.
- La versión vive en **un solo lugar por componente** (tabla 2) y el tag de Git se crea al desplegar, nunca a mano.
- Antes del primer despliegue no se sube el número: los PR acumulan cambios sobre la versión en curso.
- El título del PR (clave Jira) es la nota de versión; el changelog se arma con los PR fusionados entre dos tags.

## 2. Componentes

| Componente | Fuente de la versión | Tag de Git | Artefacto | Regla propia |
|---|---|---|---|---|
| API (`musubi-api`) | `<Version>` en `Musubi.Api/Musubi.Api.csproj` | `api-vX.Y.Z` | Imagen `musubi-api:X.Y.Z` | La ruta `api/v1` es independiente: solo cambia a `v2` con un cambio incompatible del contrato y convive con `v1` mientras exista una app que la use |
| App Android (`musubi-app`) | `version: X.Y.Z+N` en `pubspec.yaml` | `app-vX.Y.Z+N` | AAB firmado | `N` (`versionCode`) crece en **cada** subida a Google Play, también entre pistas; nunca se reutiliza |
| Esquema de datos (MongoDB) | Documento `{ _id: "schema", version: N }` en la colección `_meta` | Va con el tag de la API | Script `mongosh` numerado (`NNN-<slug>.js`) | Entero creciente; cada script es idempotente y sube `N` en 1 |
| Infraestructura (Terraform) | `infra/VERSION` | `infra-vX.Y.Z` | Plan de Terraform | `MAJOR` si recrea recursos con datos (balanceador, base, bucket) |
| Landing page | `package.json` o `VERSION` del repo de la landing | `web-vX.Y.Z` | Sitio estático | Publica el enlace a Google Play y el aviso de privacidad vigente |

## 3. Matriz de compatibilidad

Una fila por despliegue. La app declara la versión mínima de API que necesita; la API declara la versión de esquema que espera.

| Fecha | API | Ruta | Esquema | App (versión+código) | Pista de Google Play | Infra | Notas |
|---|---|---|---|---|---|---|---|
| 20-Oct-2026 | 0.1.0 | `v1` | — | 0.1.0+1 | — (sin publicar) | — | Fundación + stack NoSQL (SCRUM-66). Sin despliegue todavía |
| 25-Oct-2026 | 0.2.0-alpha.1 | `v1` | 1 | 0.1.0+1 | — (sin publicar) | 0.1.0 | Primer despliegue en AWS (SCRUM-80): registro, verificación, sesión y recuperación de contraseña (HU-01/02/03/47/56) |
| 27-Oct-2026 | 0.3.0-alpha.1 | `v1` | 2 | 0.2.0+2 | — (sin publicar) | 0.2.0 | Publicar productos y vitrina pública (SCRUM-23, HU-06/50 y HU-48 fase 2): fotos en S3 + CloudFront, categorías y productos (`db/002-publicaciones.js`) |
| 29-Oct-2026 | 0.3.0-alpha.2 | `v1` | 2 | 0.2.0+2 | — (sin publicar) | 0.3.0 | Dominio propio (SCRUM-85): `api.musubiapp.com.mx` con certificado ACM y TLS 1.2 como mínimo, y correo desde `no-reply@musubiapp.com.mx`. API sin cambios funcionales: dependencias de Dependabot (Scalar 2.18.2, AWSSDK.S3 4.0.104.6, AWSSDK.SimpleEmailV2 4.0.106.6, imágenes .NET 10.0.14 / SDK 10.0.403) |
| 30-Oct-2026 | 0.3.0-alpha.2 | `v1` | 2 | 0.3.0+3 | Prueba interna | 0.3.0 | Primera versión en Google Play (SCRUM-87): AAB firmado con la llave de subida y Play App Signing, contra `https://api.musubiapp.com.mx` |
| 30-Oct-2026 | 0.3.0-alpha.3 | `v1` | 3 | 0.3.0+3 | Prueba interna | 0.3.0 | Cuenta de revisión de Google Play (SCRUM-88): dominio `musubiapp.com.mx` en la UTTT (`db/003-dominio-revision.js`); sin otros cambios en la API |
| 30-Oct-2026 | 0.3.0-alpha.4 | `v1` | 3 | 0.3.0+4 | Prueba cerrada | 0.3.1 | Eliminar mi cuenta desde la app (SCRUM-32, HU-46): `POST /api/v1/account/delete` con la contraseña actual, borrado real de la cuenta, las sesiones, las publicaciones y las fotos; `s3:ListBucket` en las fotos |

## 4. Plan hacia la evaluación

| Hito | API | App | Pista |
|---|---|---|---|
| Fin de S3 (08-Nov-2026) | 0.3.0 | 0.3.0+N | Prueba interna |
| Prueba cerrada (desde el 09-Nov-2026) | 0.4.0-beta.N | 0.4.0-beta.N+N | Prueba cerrada |
| Candidata (fin de S5) | 1.0.0-rc.N | 1.0.0-rc.N+N | Prueba cerrada o producción |
| Evaluación (02-Dic-2026) | 1.0.0 | 1.0.0+N | Producción |

## 5. Aplicación en cada despliegue

- NOTE (musubi-api:02-05): el workflow de despliegue lee `<Version>`, falla si el tag `api-vX.Y.Z` ya existe, publica la imagen con ese tag, crea el tag y agrega la fila de la tabla 3.
- NOTE (musubi-app:02-02): el workflow de Google Play lee `pubspec.yaml`, falla si `versionCode` no es mayor que el último publicado, sube el AAB, crea el tag y agrega la fila de la tabla 3.
- Hasta que existan esos workflows, la fila se agrega a mano en el PR que despliega.
<!-- Fin de sección compartida -->
