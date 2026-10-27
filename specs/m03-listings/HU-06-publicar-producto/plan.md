# plan.md — HU-06 Publicar un producto

1. **Componentes:**
   - `Platform/Media/` (almacén, procesador y controlador de Development);
   - `Modules/Listings/` (servicio, controladores, errores y datos);
   - `IMemberDirectory` en M-01;
   - `db/002-publicaciones.js`;
   - `user-data` de Terraform.
2. **Decisiones:**
   - **Primero se suben las fotos y después se publica** con sus llaves: la app muestra el avance foto por foto y un error de red no pierde lo escrito. La llave incluye el id del usuario, así nadie publica fotos ajenas.
   - **La API vuelve a codificar cada foto:** la app ya la reduce (RNF-13), pero la API no confía en lo que llega. Así quita los metadatos (GPS, modelo de la cámara) y deja un formato y un tamaño uniformes para CloudFront.
   - **Rutas relativas (`/media/...`):** CloudFront sirve la API y las imágenes en el mismo dominio. La app arma la URL con su `API_BASE_URL`, y en Development la misma ruta la sirve la API.
   - **Vitrina pública en el mismo endpoint:** sin sesión no se ve el vendedor; con sesión, su nombre corto (copia R8, sin joins).
   - **Rango de precios con percentiles interpolados** y mínimo de 3 publicaciones: no se inventa un rango.
3. **Excepción al stack (00-01), aprobada por el usuario el 27-Oct-2026:**

   | Paquete | Versión | Licencia | Mantenimiento | Seguridad | Alternativa descartada | Plan de remoción |
   |---|---|---|---|---|---|---|
   | `SkiaSharp` + `SkiaSharp.NativeAssets.Linux.NoDependencies` | 4.153.1 (30-Sep-2026, estable) | MIT | Mono/.NET Foundation; releases mensuales | NuGetAudit sin avisos; restauración con caché vacía sin NU1100 | Sin librería: validar JPEG y borrar el EXIF a mano. No reduce, no convierte y las fotos verticales saldrían giradas. ImageSharp tiene una licencia dividida y Magick.NET es mucho más pesado | Reemplazar `PhotoProcessor` (una clase) |
   | `AWSSDK.S3` | 4.0.104.4 (14-Oct-2026; respeta el `cooldown` de 7 días) | Apache-2.0 | AWS; misma familia que `AWSSDK.SimpleEmailV2` | NuGetAudit sin avisos | URL prefirmadas sin SDK: más código y sin el reproceso de la foto | Reemplazar `S3MediaStorage` |

4. **Guardarraíles:**
   - rate limit `uploads` por usuario;
   - límite de 10 MB por foto (Kestrel y `RequestSizeLimit`);
   - límite de píxeles contra bombas de descompresión;
   - `$jsonSchema` estricto en `products`;
   - `MediaOptions` falla al arrancar sin bucket fuera de Development.
5. **Validación:** las pruebas de la `spec.md`, `check_endpoints`, critic y el despliegue a AWS con prueba desde el celular.
6. **Secuencia:** plataforma de imágenes → datos y script → servicio y controladores → pruebas → documentación → despliegue.
