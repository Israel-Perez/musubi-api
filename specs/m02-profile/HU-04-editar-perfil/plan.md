# plan.md — HU-04 Editar mi perfil

## 1. Componentes
- `Modules/Profiles/`:
  - `ProfileService` y `ProfilePhotoService`;
  - `ProfilesController` (`/profiles/me…` y `/profiles/{id}`) y `CareersController`, cada controlador en su archivo (`check_endpoints`);
  - `ProfilesErrors` y `ProfilesDtos`;
  - `ProfilesModuleServiceCollectionExtensions`;
  - `Data/` (documentos, contratos, `Mongo*` e `InMemory*`).
- M-01:
  - `IMemberDirectory` devuelve `MemberAccount` (nombre, correo, `emailVerifiedAt`, campus y `academicTerm`);
  - `PasswordChangeService` y la acción `change` en `PasswordController`.
- `Platform/Media`:
  - `PhotoProcessor.TryProcessSquare(bytes, 512, out photo)`;
  - `IMediaStorage.DeleteAsync` en S3 y en memoria.
- `db/003-perfiles.js` y `MongoSchema.ExpectedVersion = 3`.

## 2. Contratos (DTOs)

| Ruta | Request | Response |
|---|---|---|
| `GET /profiles/me` | — | `MyProfileResponse` |
| `PUT /profiles/me` | `UpdateProfileRequest { career: slug \| null, term: int \| null }` | `MyProfileResponse` |
| `PUT /profiles/me/visibility` | `VisibilityRequest { career: bool, sales: bool, online: bool }` | `MyProfileResponse` |
| `PUT /profiles/me/photo` | `multipart/form-data` con `photo` (JPG, PNG o WebP; hasta 10 MB) + rate limit `uploads` | `MyProfileResponse` |
| `DELETE /profiles/me/photo` | — | 204 (idempotente) |
| `GET /profiles/{id}` | — | `PublicProfileResponse` |
| `GET /careers` | — | `CareersResponse` |
| `POST /auth/password/change` | `ChangePasswordRequest { currentPassword, newPassword }` + rate limit `auth` | `SessionResponse` (sesión nueva) |

Formas de las respuestas:
- **`MyProfileResponse`**:
  - `id`, `fullName`, `email`, `emailVerifiedAt`;
  - `campus { shortName, name }`;
  - `academicTerm { unit, count }`;
  - `career { slug, name, shortName, firstTerm, lastTerm } | null`;
  - `term | null`;
  - `photo` (ruta `/media/...`) `| null`;
  - `visibility { career, sales, online }`;
  - `stats { ratingAvg | null, ratingCount, sales, purchases }`.
- **`PublicProfileResponse`**:
  - `id`, `fullName`, `campus`, `photo`, `memberSince`;
  - `career` y `term`: `null` si `visibility.career` está apagado;
  - `stats { ratingAvg, ratingCount, sales }`, con `sales` en `null` si `visibility.sales` está apagado.
- **`CareersResponse`**:
  - `academicTerm { unit, count }`;
  - `careers: [{ slug, name, shortName, level, firstTerm, lastTerm }]`, en el orden de la semilla.

## 3. Datos (`db/003-perfiles.js`)
- **`profiles`** (`_id` = `userId`; R1, R4, R13). Campos:
  - `communityId`, `fullName` (copia R8 del nombre, que no cambia);
  - `careerSlug`, `term` (entero ≥ 1), `avatarKey`;
  - `visibility { career, sales, online }`;
  - `stats { ratingAvg, ratingCount, sales, purchases }`;
  - `updatedAt`, `schemaVersion`.
  - Índice: `ix_community`.
  - Lo crea el primer cambio (upsert). Sin documento se responden los valores por defecto.
- **`careers`**:
  - campos: `communityId`, `slug`, `name`, `shortName`, `level` (`tsu`/`engineering`/`bachelor`), `firstTerm`, `lastTerm`, `order`, `active`;
  - índice `ux_community_slug`;
  - es la misma forma que `categories`, para que HU-41 administre las dos igual.
- **`communities`:** `academicTerm { unit: "cuatrimestre", count: 11 }` para la UTTT. El `$jsonSchema` lo hace opcional, y por defecto vale `{ "semestre", 12 }`.

## 4. Semilla de carreras de la UTTT (propuesta, a validar)
Fuente: [Programas Educativos de la UTTT](https://www.uttt.edu.mx/ProgramasEducativos.html), consultada el 27-Oct-2026. Los nombres se normalizaron, corrigiendo erratas del sitio como "Mercadoctecnia" y "Proceso". `shortName` es lo que se ve en el perfil.

- **TSU (cuatrimestres 1–6), 22:**
  - Energías Renovables, área Solar;
  - Química, área Industrial;
  - Química, área Tecnología Ambiental;
  - Nanotecnología, área Materiales;
  - Mantenimiento Industrial, área Industrial;
  - Mantenimiento Industrial, área Maquinaria Pesada;
  - Mantenimiento, área Soldadura;
  - Contaduría;
  - Administración, área Capital Humano;
  - Administración, área Formulación y Evaluación de Proyectos;
  - Procesos Industriales, área Manufactura;
  - Construcción y Montaje de Plantas Industriales, área Hidrocarburos;
  - Logística, área Transporte Terrestre;
  - Logística, área Cadena de Suministros;
  - Desarrollo de Negocios, área Ventas;
  - Desarrollo de Negocios, área Mercadotecnia;
  - Mecatrónica, área Instalaciones Eléctricas Eficientes;
  - Mecatrónica, área Robótica;
  - Mecatrónica, área Automatización;
  - Tecnologías de la Información, área Desarrollo de Software Multiplataforma;
  - Tecnologías de la Información, área Entornos Virtuales y Negocios Digitales;
  - Tecnologías de la Información, área Infraestructura de Redes Digitales.
- **Ingeniería (cuatrimestres 7–11), 11:**
  - Energías Renovables;
  - Química;
  - Nanotecnología;
  - Ambiental;
  - Mantenimiento Industrial;
  - Procesos y Operaciones Industriales;
  - Construcción y Montaje de Plantas Industriales;
  - Mecatrónica;
  - Desarrollo y Gestión de Software;
  - Entornos Virtuales y Negocios Digitales;
  - Redes Inteligentes y Ciberseguridad.
- **Licenciatura (cuatrimestres 7–11), 5:**
  - Contaduría;
  - Gestión del Capital Humano;
  - Gestión de Negocios y Proyectos;
  - Diseño y Gestión de Redes Logísticas;
  - Innovación de Negocios y Mercadotecnia.
- **Pendientes de confirmar con el equipo** (no se siembran):
  - Gastronomía;
  - Enfermería (Tepetitlán);
  - Agricultura Sustentable y Protegida (Chapulhuacán);
  - Clasificación Arancelaria y Despacho Aduanero;
  - Turismo (Chapulhuacán);
  - los 3 posgrados.

## 5. Decisiones
- **Un perfil aparte de `users`** (R10): las credenciales nunca viajan junto al perfil y la lectura pública no toca `users`.
- **Perfil perezoso:** no hace falta migrar a quienes ya tienen cuenta. Sin documento, el perfil son los valores por defecto.
- **La visibilidad se aplica en la API**, no solo en la app: si la app la ignorara, los datos seguirían protegidos (OWASP API3, exposición excesiva de datos).
- **El periodo valida contra la carrera:** un TSU no puede estar en el 9.º cuatrimestre. Sin carrera, el periodo va del 1 al tope del campus.
- **Foto cuadrada de 512 px:** basta para el avatar más grande, que mide 72 pt a 3×, y pesa poco (RNF-13). Se borra la anterior para no dejar datos personales sin dueño en S3 (R11).
- **`PUT /profiles/me/photo` y no `POST`:** cada persona tiene una sola foto, así que repetir la llamada la reemplaza.
- **Cambiar la contraseña revoca todo** y devuelve una sesión nueva: si alguien más tenía la sesión, la pierde. La app reemplaza sus tokens sin volver a pedir el login.
- **`auth.wrong_password` responde 400** y no 401: la app trata un 401 como sesión vencida y la renovaría.
- **Sin paquetes nuevos:** SkiaSharp y AWSSDK.S3 ya están aprobados (HU-06).

## 6. Guardarraíles
- Rate limits:
  - `uploads` en la foto;
  - `auth` en el cambio de contraseña;
  - el límite de 10 MB por foto.
- `$jsonSchema` estricto en `profiles` y `careers`.
- Las rutas de escritura no llevan id: solo se edita el perfil del token (OWASP API1).
- Perfil público solo dentro del campus. Si no es visible, responde 404 y no 403, para no revelar que la cuenta existe.

## 7. Validación
Las pruebas de `spec.md`, `check_endpoints`, critic, el despliegue a AWS (`api-v0.4.0-alpha.1`, script `003`) y la prueba desde el celular.

## 8. Secuencia
1. Datos y script.
2. `IMemberDirectory` y la plataforma de fotos.
3. Servicio y controladores.
4. Cambio de contraseña.
5. Pruebas.
6. Documentación.
7. Despliegue (con autorización del usuario).
