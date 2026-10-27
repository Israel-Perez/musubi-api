# HU-06 — Publicar un producto (y la vitrina pública)

**HU / Jira / Sprint / Módulo / Contraparte:** HU-06 / SCRUM-23 / S2 / M-03 (más HU-50 y la fase 2 de HU-48, M-04) / [musubi-app:HU-06](https://github.com/Israel-Perez/musubi-app/tree/main/specs/m03-listings/HU-06-publicar-producto)

## Contexto
Quien vende publica un producto con fotos, nombre, descripción, precio, categoría, estado y piezas disponibles (maqueta `Publicar`, `Vender-1…4` y `Vender-listo`). La publicación aparece al instante en la vitrina del campus, también para visitantes.

Esta spec incluye la lectura pública del catálogo (fase 2 de HU-48) y la cantidad al publicar (HU-50). Sin ellas, lo publicado no se vería en ningún lado. Es la excepción documentada a "un PR = una HU".

**Depende de:** `02-01`, `02-03`, `02-05` (bucket de imágenes y CloudFront) y `HU-01`/`HU-03` (solo publica una cuenta verificada).

## Requisitos
1. **Fotos (RF-07, RNF-13, RNF-05):**
   - se suben una por una antes de publicar (JPG, PNG o WebP; hasta 10 MB);
   - la API las decodifica, las endereza según el EXIF, las reduce a 1600 px por lado y las guarda como WebP **sin metadatos**, así la ubicación GPS de la cámara nunca llega a S3;
   - menos de 200 px por lado o más de 50 MP se rechaza;
   - tope de 30 subidas por minuto por usuario.
2. **Publicar (RN-03, RN-04):**
   - obligatorios: nombre (3–80), precio (mayor que cero, hasta $999,999), categoría del campus, estado (nuevo, o usado con detalle: como nuevo, buen estado o con detalles), piezas (1–10) y de 1 a 8 fotos;
   - descripción opcional (hasta 500);
   - solo se aceptan fotos que subió la misma persona (OWASP API1);
   - queda **Disponible**.
3. **Vitrina pública (HU-48):**
   - lo recién publicado del campus, del más nuevo al más viejo, paginado por cursor;
   - filtros "Filtrar": categoría y "Ocultar agotados";
   - incluye el total;
   - sin sesión no se ve el vendedor (RNF-05); con sesión, su nombre corto ("Ana R.").
4. **Mis productos:** las publicaciones propias, también pausadas y agotadas.
5. **Rango de precios** (`Vender-3`): percentiles 25 y 75 de las publicaciones activas de la categoría. Con menos de 3 no se muestra nada inventado (204).
6. **Categorías:** las 6 de la maqueta para la UTTT (Calculadoras, Libros, Laptops, Laboratorio, Dibujo y Electrónica), sembradas por el script. HU-41 las administrará.

## Diseño
- **Plataforma `Platform/Media/`:**
  - `IMediaStorage`: S3 en la nube y en memoria en Development; `MediaOptions` valida al arrancar;
  - `PhotoProcessor` (SkiaSharp);
  - `MediaController` sirve `/media/*` solo en Development. En la nube CloudFront sirve `/media/*` desde el bucket privado (02-05).
- **Módulo `Modules/Listings/`:**
  - `ListingService` (publicar, vitrina, mis productos, rango) y `PhotoUploadService`;
  - `ProductsController` y `CategoriesController`;
  - `ListingsErrors`;
  - `Data/`: documentos, contratos, `Mongo*` e `InMemory*`.
- **M-01 expone `IMemberDirectory`** (campus por `slug` y datos mínimos del miembro): un módulo no lee las colecciones de otro.
- **Datos:**
  - `db/002-publicaciones.js` crea `categories` y `products` con su `$jsonSchema`, sus índices y la semilla;
  - `MongoSchema.ExpectedVersion = 2`;
  - filas en [`docs/modelo-datos.md`](../../../docs/modelo-datos.md).
- **Infraestructura:** `user-data` pasa `Media__Provider`, `Media__Bucket` y `Media__Region`. El rol de la instancia ya podía escribir en el bucket.
- **Pendientes:**
  - `NOTE (HU-18)`: avisar en segundo plano a las solicitudes que coincidan (RF-23);
  - `NOTE (HU-08)`: paginar "Mis productos" pasando de 50;
  - moderación de imágenes y texto con 02-07;
  - fotos subidas que nunca se publican: regla de ciclo de vida del bucket con 02-05.

## Contratos
- **Filas** en [`docs/endpoints.md`](../../../docs/endpoints.md):
  - `GET /api/v1/categories`;
  - `GET /api/v1/products`;
  - `GET /api/v1/products/mine`;
  - `GET /api/v1/products/price-range`;
  - `POST /api/v1/products`;
  - `POST /api/v1/products/photos`;
  - `GET /media/{**path}` (solo Development).
- **Fixtures:** `ContractTest/Fixtures/{photo,product,product-page,category}.json`.
- **Errores:** `listings.*` en [`docs/errores.md`](../../../docs/errores.md).

## Tests
- `UnitTest/Listings/PhotoProcessorTests.cs` (5):
  - EXIF de teléfono con orientación 6 y GPS: sale girada y sin metadatos;
  - la salida es WebP;
  - se reduce a 1600 px;
  - se rechazan la foto diminuta y el archivo que no es imagen.
- `TestShared/Listings/ListingRepositoryContract.cs` (7), en memoria (`UnitTest`) y en MongoDB real (`DatabaseTest`):
  - categorías sembradas;
  - orden y cursor;
  - campos de ida y vuelta;
  - filtros;
  - "mis productos" sin los quitados;
  - precios activos.
- `DatabaseTest/SchemaScriptTests.cs`: índices de `products`, el validador rechaza un precio de 0 y el script es idempotente con `002`.
- `IntegrationTest/Listings/ListingFlowTests.cs` (7):
  - subir y publicar: aparece en la vitrina y en "mis productos", y la foto se sirve como WebP;
  - cada validación con su código;
  - foto ajena rechazada;
  - archivo que no es foto;
  - filtro y cursor;
  - categorías y campus desconocido;
  - rango de precios con 3 publicaciones.
- `SecurityTest/Auth/ListingsAuthTests.cs` (7):
  - 401 sin sesión en publicar, subir, "mis productos" y rango;
  - la vitrina y las categorías son públicas;
  - un token válido de quien no es miembro no publica (403);
  - en la nube la API no sirve `/media`.
- `ContractTest/ListingsContractTests.cs` (1): formas de foto, producto, página y categoría.

## Criterios
- `dotnet build -c Release` 0/0.
- Unit 104 · Integration 53 · Security 49 · Contract 6 (Database en el CI).
- `check_endpoints` OK (19 filas).
- critic PASS.
- Restauración con caché vacía sin NU1100.

## Límites
- **Una sola comunidad (UTTT):** el visitante elige campus cuando haya más (RNF-21).
- **"Populares" (HU-49):** necesita las vistas del detalle (HU-11); hasta entonces la app no lo muestra.
- **Fuera de alcance:** editar, pausar y borrar publicaciones (HU-07/08), y descontar inventario al vender (HU-50 con HU-52/23).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 27-Oct-2026
- **Detalle:**
  - excepción de stack aprobada por el usuario (27-Oct-2026): SkiaSharp para las fotos y AWSSDK.S3 (ver `plan.md`);
  - "Populares" oculto hasta HU-11, por decisión del usuario.
