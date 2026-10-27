# HU-04 — Editar mi perfil

**HU / Jira / Sprint / Módulo / Contraparte:** HU-04 / SCRUM-22 / S1 (se ejecuta en S3) / M-02 / [musubi-app:HU-04](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m02-profile/HU-04-editar-perfil)

## Contexto
Jira (SCRUM-22): "Como **usuario**, quiero **editar mi perfil**, para **mantener mi información actualizada**". Sus criterios son tres:
- consultar y editar la información del perfil;
- pedir solo los datos necesarios (RNF-05);
- que los cambios se vean de inmediato en el perfil público.

La historia dejaba pendientes los campos visibles (RF-04, "[POR DEFINIR]"). La maqueta los resuelve:
- **`Perfil`**: foto o iniciales, nombre, "UTTT · carrera · periodo", "Verificada", estadísticas y accesos.
- **`Ajustes`**:
  - la tarjeta de la cuenta abre "Datos personales" (el nombre no se edita, la carrera y el periodo sí);
  - "Correo institucional" (no se puede cambiar);
  - "Cambiar contraseña".
- **`Privacidad` → "Quién te ve"**: mostrar mi carrera, mis ventas y si estoy en línea.

**Decisiones del usuario (27-Oct-2026):**
- **Periodo:** el periodo es el **cuatrimestre**, porque la UTTT cursa 11: TSU del 1 al 6 e ingeniería o licenciatura del 7 al 11. Cada campus define su periodo, para que otra universidad pueda usar semestres (RNF-21).
- **Carrera:** se elige de una **lista del campus**, no se escribe.
- **Alcance:** entran las cuatro piezas de la maqueta ("Quién te ve", Perfil completo, "Correo institucional" y "Cambiar contraseña"), **más una foto de perfil**. La foto es un agregado permitido por el lineamiento 4.

**Depende de:** `02-01` (modelo de datos), `02-03` (sesión y errores), `HU-01`/`HU-03` (cuenta verificada) y `HU-06` (`Platform/Media`: procesamiento de fotos y S3).

## Requisitos
1. **Consultar mi perfil (RF-04):**
   - nombre, correo, fecha de verificación, campus, carrera, periodo, foto, visibilidad y estadísticas;
   - si la persona nunca editó su perfil, responde con valores por defecto y no crea ningún documento.
2. **Datos personales (RNF-05):**
   - solo se editan la carrera (de la lista del campus) y el periodo (del 1 al tope del campus, dentro del rango de la carrera); los dos son opcionales y se pueden borrar;
   - el nombre y el correo **no se editan**: vienen de la cuenta verificada (`Ajustes`, "Viene de tu correo institucional verificado").
3. **Carreras y periodo del campus:**
   - un endpoint devuelve las carreras activas del campus del token y su periodo (nombre y número de periodos);
   - la semilla de la UTTT trae 38 carreras con su rango de cuatrimestres: TSU del 1 al 6, ingeniería y licenciatura del 7 al 11.
4. **Foto de perfil (RNF-05, RNF-13):**
   - es opcional;
   - pasa por el mismo procesamiento de HU-06: se endereza según el EXIF, se le **quitan los metadatos** y se guarda como WebP; además se recorta cuadrada al centro y se reduce a 512 px;
   - al reemplazarla o quitarla, se borra el archivo anterior de S3;
   - comparte el tope de subidas de HU-06 (30 por minuto por usuario).
5. **Quién te ve:**
   - tres interruptores (`career`, `sales`, `online`), encendidos por defecto como en la maqueta;
   - la API los **aplica en el servidor**: el perfil público nunca devuelve la carrera ni el periodo si `career` está apagado, ni las ventas si `sales` está apagado.
   - `online` se guarda desde ahora; lo aplicará HU-20 con el chat.
6. **Perfil público (criterio 3 de Jira):**
   - solo lo ve gente del mismo campus ("Tu perfil solo lo ve tu campus");
   - de otro campus o inexistente responde lo mismo (404), sin revelar si la cuenta existe;
   - lee el documento del perfil sin copias intermedias, así que un cambio se ve en la siguiente consulta.
   - HU-05 le agrega reputación, publicaciones y opiniones.
7. **Cambiar contraseña (`Ajustes`):**
   - pide la actual y la nueva, con la misma política de HU-01;
   - al cambiarla **cierra todas las demás sesiones** y devuelve una sesión nueva (OWASP ASVS V3.3);
   - si la contraseña actual no coincide, responde 400 y no 401, para que la app no intente renovar la sesión;
   - lleva rate limit.

## Diseño
- **Módulo `Modules/Profiles/`:**
  - `ProfileService`: consultar, editar, visibilidad y perfil público;
  - `ProfilePhotoService`: subir y quitar la foto;
  - `ProfilesController`, `CareersController` y `ProfilesErrors`;
  - `Data/`: `ProfileDocument`, `CareerDocument`, sus repositorios `Mongo*`/`InMemory*` y una batería compartida en `TestShared/Profiles/`.
- **M-01 expone en `IMemberDirectory`** lo que M-02 necesita de la cuenta: nombre, correo, fecha de verificación, campus y su periodo. Un módulo no lee las colecciones de otro.
- **Cambiar contraseña vive en M-01:**
  - endpoint en `PasswordController`;
  - servicio en `PasswordChangeService`;
  - reutiliza `Passwords`, `RefreshTokenService` y `SessionIssuer`.
- **`Platform/Media`:**
  - `PhotoProcessor` gana una variante cuadrada (recorte al centro, 512 px);
  - `IMediaStorage` gana `DeleteAsync`, que también usará HU-46;
  - llave de la foto: `media/avatars/{userId}/{32hex}.webp`.
- **Datos:**
  - `db/003-perfiles.js` crea `profiles` y `careers` con su `$jsonSchema` e índices;
  - agrega `academicTerm` a `communities`;
  - siembra las carreras de la UTTT;
  - `MongoSchema.ExpectedVersion = 3`;
  - filas en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).
- **Pendientes:**
  - `NOTE (HU-05)`: reputación, publicaciones y opiniones en el perfil público;
  - `NOTE (HU-20)`: aplicar `online`;
  - `NOTE (HU-23)`/`NOTE (HU-27)`: actualizar `stats` al completar pedidos y calificaciones;
  - `NOTE (HU-46)`: borrar el perfil y la foto al eliminar la cuenta;
  - `NOTE (02-07)`: moderar la foto;
  - `NOTE (HU-41)`: administrar las carreras.

## Contratos
- **Filas nuevas** en [`docs/endpoints.md`](../../../docs/endpoints.md), que se agregan junto con el código (`check_endpoints`):
  - sección M-02:
    - `GET /api/v1/profiles/me`;
    - `PUT /api/v1/profiles/me`;
    - `PUT /api/v1/profiles/me/visibility`;
    - `PUT /api/v1/profiles/me/photo`;
    - `DELETE /api/v1/profiles/me/photo`;
    - `GET /api/v1/profiles/{id}`;
    - `GET /api/v1/careers`;
  - sección M-01: `POST /api/v1/auth/password/change`.
  - La forma de cada DTO está en `plan.md`.
- **Errores nuevos** en [`docs/errores.md`](../../../docs/errores.md):
  - `profiles.invalid_career` (400);
  - `profiles.invalid_term` (400);
  - `profiles.not_found` (404);
  - `auth.wrong_password` (400).
  - Las fotos reutilizan `listings.photo_*`.
- **Fixtures:** `ContractTest/Fixtures/{my-profile,public-profile,careers}.json`.
- **Configuración:** sin claves nuevas. La foto usa `Media:*` de HU-06.

## Tests
- `UnitTest/Profiles/ProfileServiceTests.cs`:
  - perfil por defecto sin documento;
  - carrera de otro campus;
  - periodo fuera del rango de la carrera;
  - borrar carrera y periodo;
  - el perfil público oculta lo que la visibilidad apaga.
- `UnitTest/Media/PhotoProcessorTests.cs` (+2): la variante cuadrada sale de 512 × 512 desde una foto vertical con EXIF 6, y sin metadatos.
- `TestShared/Profiles/ProfileRepositoryContract.cs`, en memoria (`UnitTest`) y en MongoDB real (`DatabaseTest`):
  - upsert;
  - visibilidad;
  - foto;
  - carreras activas del campus en su orden.
- `DatabaseTest/SchemaScriptTests.cs` (+1):
  - índices de `profiles` y `careers`;
  - el validador rechaza un periodo de 0;
  - el script es idempotente con `003`.
- `IntegrationTest/Profiles/ProfileFlowTests.cs`:
  - editar y ver el cambio al instante en el perfil público de otra persona del campus;
  - apagar "Mostrar mi carrera" la oculta;
  - subir, reemplazar (se borra la anterior) y quitar la foto;
  - cada validación con su código.
- `IntegrationTest/Auth/PasswordChangeTests.cs`:
  - cambia y entra con la nueva;
  - las sesiones anteriores quedan revocadas;
  - con la actual incorrecta responde 400 `auth.wrong_password`;
  - con la nueva débil responde 400 `auth.weak_password`.
- `SecurityTest/Auth/ProfilesAuthTests.cs`:
  - 401 sin sesión en todos;
  - el perfil de otro campus responde 404;
  - nadie edita la foto ni los datos de otra persona: no hay ruta con id para escribir.
- `ContractTest/ProfilesContractTests.cs`: formas de mi perfil, perfil público y carreras.

## Criterios
- `dotnet build -c Release` 0/0.
- Unit, Integration, Security y Contract al 100 %; Database en el CI.
- `check_endpoints` OK (19 → 27 filas).
- critic PASS.
- Restauración con caché vacía sin NU1100 (sin paquetes nuevos).
- Desplegado con `infra-up` y probado desde el celular contra AWS:
  - editar la carrera y el cuatrimestre;
  - cambiar la foto;
  - apagar "Mostrar mi carrera";
  - cambiar la contraseña.

## Límites
- **El nombre no se edita** (maqueta). Un nombre mal escrito al registrarse se corrige por soporte (HU-40).
- **La lista de carreras es una propuesta** tomada del sitio oficial de la UTTT. El equipo debe validarla antes de cerrar la HU.
  - Quedan fuera hasta confirmar su nivel: Gastronomía, Enfermería, Agricultura Sustentable y Protegida, Clasificación Arancelaria y Despacho Aduanero, Turismo y los 3 posgrados.
  - Ver `plan.md`.
- **El periodo es por campus**, pero solo se configura en el script de la base. HU-41 lo administrará.
- **Fuera de alcance:**
  - la pantalla del perfil de otra persona (`Perfil-vendedor`, HU-05);
  - usuarios bloqueados (HU-28);
  - descargar mis datos y eliminar la cuenta (HU-46);
  - preferencias y modo noche (HU-54);
  - cuenta para cobrar (HU-55).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 27-Oct-2026
- **Detalle:**
  - alcance y decisiones acordados con el usuario el 27-Oct-2026: cuatrimestre configurable por campus, carrera de una lista, las cuatro piezas de la maqueta y la foto;
  - sin paquetes nuevos.
