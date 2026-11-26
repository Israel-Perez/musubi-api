# Lineamientos de la materia — proyecto móvil (sep-dic 2026)

Transcripción del documento "Lineamientos para el desarrollo del proyecto Móvil", cuatrimestre septiembre-diciembre 2026, recibido el 20-Oct-2026. Son **criterios de evaluación**: si la constitución, una spec o la plantilla inicial los contradicen, se corrige el artefacto, no el lineamiento (constitución §1). El checklist (sección 2) es el canónico; `musubi-app` lo enlaza y no lo copia.

## 1. Texto

1. Para iniciar la evaluación se descarga la app desde Google Play.
2. Después de la evaluación interactiva, el proyecto se evalúa con prompts que verifican que todo lo solicitado esté implementado.
3. El proyecto se compone de: nube, base de datos, backend, móvil y landing page.
4. La app móvil se desarrolla a la imagen de la maqueta. Se pueden agregar funciones si el proyecto lo requiere, **nunca eliminar** funcionalidades.
5. Uso obligatorio de Spec Driven Development y DevSecOps en backend y móvil.
6. Backend y móvil se desarrollan enteramente con agentes de IA (sugerido: OpenCode con la interfaz WARP).
7. Backend y móvil implementan pruebas unitarias, de seguridad y de integración, además de los demás procesos de DevSecOps.
8. CI/CD en backend y móvil.
9. Caché a nivel de base de datos, backend y frontend.
10. Logout en backend y móvil.
11. Antes del desarrollo, una matriz de numeración de versiones, aplicada en cada despliegue.
12. Se pueden usar MCP cuando los servicios externos lo ofrezcan.
13. **Nube:** todos los servicios configurados en la nube (la base de datos puede ser un servicio externo); load balancer o similar, usado desde el backend; IaC recomendado con Terraform para crear y eliminar la infraestructura; salida del backend con SSL (HTTPS).
14. **Base de datos:** de tipo NoSQL; el número de bases depende del proyecto.
15. **Backend:** escalable, en al menos 2 instancias; solo en AWS, Azure o Google Cloud, **con costo cero**; todas las funciones de la API en la nube elegida; endpoints eficientes; API o microservicios a elección; imágenes en el CDN de la nube; servicio de observabilidad en la nube (log de eventos y errores); JWT para la autenticación; rendimiento: caching, rate limit, middleware, paginación, índices, serializadores JSON y compresión; patrones de integración según el proyecto; un paso automatizado para crear la infraestructura y desplegar, y otro para eliminar lo desplegado y la infraestructura.
16. **Móvil:** despliegue sugerido desde GitHub a Google Play; texto, imágenes y videos se evalúan con una API de la nube antes de guardarse y no se permite subir lo inapropiado; notificaciones push en los procesos principales; aviso de privacidad y manejo de datos en la app, enlazado a páginas de la landing; eliminar cuenta; permisos del móvil; sesión siempre viva salvo logout; SQLite local con sincronización si el proyecto lo requiere; al menos una función de ML y una de IA generativa.
17. **Landing page:** hosting libre; funcionalidad básica, enlace a la tienda y aviso de privacidad y manejo de datos.
18. **GitHub:** los proyectos en una sola cuenta.
19. **Evaluación:** 02-Dic-2026 (después cuenta como remedial o extraordinario). Se entregan los proyectos en `.rar` y el script de la base de datos. Quien no colabore se separa del equipo y hace el proyecto completo por su cuenta.

## 2. Checklist

Estados: ✅ Cumplido · 🟡 Parcial · ⏳ Pendiente. "Dueño" es la spec o HU que lo cierra (`app:` = `musubi-app`).

| ID | Lineamiento | Cómo se cumple en MUSUBI | Dueño | Estado |
|---|---|---|---|---|
| L-01 | Descarga desde Google Play | Prueba cerrada desde el 09-Nov-2026 (`0.11.0+13`, `docs/versionado.md`; ficha y "Seguridad de los datos" en `musubi-app/docs/play/README.md`). Falta pasar a producción antes del 02-Dic-2026 | app:02-02 | 🟡 |
| L-02 | Evaluación con prompts | Specs SDD de cada HU y fase, `docs/trazabilidad.md` (HU ↔ spec ↔ prueba ↔ Jira, al día el 26-Nov-2026), este checklist y `AGENTS.md` en cada repo; 02-10 cerró las brechas de "Prompts para evaluar v1" | 00-02 · 02-10 | ✅ |
| L-03 | Nube, BD, back, móvil y landing | `musubi-api` (AWS), MongoDB Atlas, `musubi-app` (Google Play) y `musubi-landing` (Cloudflare, `musubiapp.com.mx`) | 02-05 | ✅ |
| L-04 | App igual a la maqueta, sin quitar funciones | Maqueta versionada en `musubi-app/docs/maqueta/` (60 pantallas); cada spec de HU cita sus pantallas y se compara con capturas (`tool/screenshots_test.dart`, 02-04). Lo que no está en la maqueta se agregó sin quitar nada. Pagos con tarjeta salió por D-01 (HU-43) | app:02-04 | ✅ |
| L-05 | SDD + DevSecOps | Constitución, plantilla, specs, critic, Semgrep, Dependabot | 00-01 | ✅ |
| L-06 | Desarrollo con agentes de IA | Claude Code (`.claude/`: agentes, skills y hooks) en lugar de OpenCode + WARP (sugerido, no obligatorio) | 00-02 | ✅ |
| L-07 | Pruebas unitarias, de seguridad y de integración (back y móvil) | Back: `UnitTest`, `SecurityTest`, `IntegrationTest`, `ContractTest`, `DatabaseTest`, mutación y rendimiento. App: unit + widget, `test/security/`, `test/integration/` contra la API real (02-10) e `integration_test/` en el teléfono. Cada endpoint tiene sus tres pruebas (`check_endpoint_tests.ps1`) | app:02-01 · 02-10 | ✅ |
| L-08 | CI/CD back y móvil | CI en los dos repos (`ci-pr.yml`); CD a AWS con `infra-up` (imagen en ECR, versión de `docs/versionado.md`) y a Google Play con `release-play` | 02-08 · app:02-05 | ✅ |
| L-09 | Caché en BD, back y front | Redis (cache-aside de categorías y carreras) y caché de salida HTTP de la vitrina (02-02, `CachingFlowTests`); en la app, SQLite con las primeras páginas para abrir sin conexión (HU-63) | 02-02 · app:02-06 | ✅ |
| L-10 | Logout en back y móvil | Revoca la familia del refresh token y el access token de inmediato (lista en Redis, 02-10); la app borra el almacenamiento seguro y la base local (`session_security_test.dart`) | HU-02 · 02-10 | ✅ |
| L-11 | Matriz de versionamiento antes del desarrollo, aplicada en cada despliegue | `docs/versionado.md` (sección compartida) + `VersioningTests` / `versioning_test.dart` | SCRUM-66 | ✅ (aplicación automática: 02-05 · app:02-02) |
| L-12 | MCP | MCP de Atlassian (Jira y Confluence) | — | ✅ |
| L-13a | Servicios en la nube; BD externa permitida | AWS (EC2, ALB, CloudFront, S3, ECR, SSM, CloudWatch, Rekognition, SES) + MongoDB Atlas M0 (`infra/`, parámetros en SSM) | 02-05 | ✅ |
| L-13b | Load balancer | Application Load Balancer (`infra/stack/compute.tf`) | 02-05 | ✅ |
| L-13c | IaC con Terraform (crear y eliminar) | `infra/bootstrap` + `infra/stack`; `infra-up` crea y `infra-down` elimina | 02-05 | ✅ |
| L-13d | Salida con SSL/HTTPS | `https://api.musubiapp.com.mx`: CloudFront solo HTTPS con certificado de ACM y TLS 1.2 (`infra/stack/edge.tf`), HSTS; la landing en `https://musubiapp.com.mx` (Cloudflare) | 02-05 | ✅ |
| L-14 | Base de datos NoSQL | MongoDB: modelo, validadores e índices en `db/001`…`db/036` (esquema 36), `DatabaseTest` contra MongoDB real | 02-01 | ✅ |
| L-15a | Al menos 2 instancias | Auto Scaling Group con 2 EC2 en 2 zonas (validación en Terraform: mínimo 2) | 02-05 | ✅ |
| L-15b | AWS/Azure/GCP con costo cero | AWS con créditos (100 USD, vencen el 20-Abr-2027) + Atlas M0 gratis; se enciende solo para usarse (`infra-up`/`infra-down`) y hay alertas de costo (`infra/README.md`) | 02-05 | ✅ |
| L-15c | Todas las funciones de la API en la nube | El monolito modular completo corre en AWS. Solo la llamada al modelo de IA sale a Gemini (ver §3, HU-32/33): decisión documentada | 02-05 | ✅ |
| L-15d | Endpoints eficientes | Índices en cada consulta (`db/`), paginación por cursor, compresión, caché y p95 medido (02-09: 143–251 ms contra AWS con caché) | 02-04 · 02-09 | ✅ |
| L-15e | API o microservicios | Monolito modular (constitución §7) | 00-01 | ✅ |
| L-15f | Imágenes en el CDN | S3 privado + CloudFront (`/media/*`, OAC); las fotos del chat van aparte, solo para los participantes (HU-65) | 02-05 · HU-06 | ✅ |
| L-15g | Observabilidad en la nube | CloudWatch: logs JSON con `TraceId` (`/musubi/prod/api`), alarmas por correo y dashboard en Terraform (`infra/stack/observability.tf`, `RequestLoggingTests`) | 02-06 | ✅ |
| L-15h | JWT | Access token HS256 de 15 min + refresh rotativo; sesión obligatoria por defecto (02-03; `SecurityTest/Auth/JwtTests.cs`) y revocación inmediata (02-10) | HU-02 | ✅ |
| L-15i | Caching, rate limit, middleware, paginación, índices, serializadores, compresión | Caché (02-02), topes por ruta con 429 (`RateLimitTests`), middleware de errores y encabezados (01-02, SCRUM-123), cursores, índices, JSON camelCase y compresión Brotli/gzip (02-10) | 02-02 · 02-04 | ✅ |
| L-15j | Patrones de integración | Adaptadores detrás de interfaces (IA, moderación, correo, push, medios), colas en memoria con workers (coincidencias, push), backplane de Redis para SignalR y contratos entre módulos (Modules/README) | Por HU | ✅ |
| L-15k | Paso automatizado para crear/desplegar y para eliminar | Workflows `infra-up` / `infra-down` (OIDC, entorno `produccion`) | 02-05 | ✅ |
| L-16a | Despliegue de GitHub a Google Play | `musubi-app/.github/workflows/release-play.yml`: valida la versión, prueba, firma y sube el AAB (primera vez `0.3.0+4`, 03-Nov-2026) | app:02-02 | ✅ |
| L-16b | Moderación de texto, imágenes y video antes de guardar | Amazon Rekognition (fotos de productos, perfil, ofertas y chat) y léxico local para el texto (Comprehend con `TextProvider=Aws`), antes de guardar; también el texto que propone la IA (HU-59). La app no sube video | 02-07 · HU-06/13/20/65 | ✅ |
| L-16c | Push en los procesos principales | FCM (API HTTP v1) desde la API: mensajes, pedidos, acuerdos, ofertas, coincidencias, solicitudes por vencer y recordatorio de entrega (HU-31, HU-19/29, HU-62) | HU-31 | ✅ |
| L-16d | Aviso de privacidad en la app y en la landing | Un solo texto (`musubi-app/assets/legal/aviso-de-privacidad.md`): pantalla en la app (HU-47) y `musubiapp.com.mx/aviso-de-privacidad` (la landing lo toma al construirse) | HU-47 · SCRUM-110 | ✅ |
| L-16e | Eliminar cuenta | En la app, Ajustes → Eliminar mi cuenta (HU-46, `POST /account/delete`, borrado real) y `musubiapp.com.mx/eliminar-cuenta` para Google Play | HU-46 | ✅ |
| L-16f | Permisos del móvil | Solo cámara y notificaciones, pedidos en contexto (al tomar una foto y al primer mensaje o solicitud); la lista permitida la cuida `test/security/android_config_test.dart` | app:02-01 · HU-31 | ✅ |
| L-16g | Sesión siempre viva salvo logout | Refresh token rotativo de 60 días que se desliza con cada uso (02-03); en la app, almacenamiento seguro e interceptor que renueva (`session_security_test.dart`) | HU-02 · app:02-01 | ✅ |
| L-16h | SQLite + sincronización (si aplica) | `sqflite`: la primera página de Inicio, Mensajes, Mis pedidos y Mis productos se abre sin conexión y se revalida; los mensajes "Por enviar" se mandan al volver la red (HU-63) | HU-63 | ✅ |
| L-16i | Al menos una función de ML y una de IA generativa | ML: "Recomendado para ti" y "Ver parecidos" con ML.NET (TF-IDF aprendido del catálogo, perfil de la persona y factorización de matrices de una clase), evaluado con leave-one-out (`RecommendationModelTests.Evaluation_LeaveOneOut_BeatsRandomByFar`; hit@5 en los logs de cada entrenamiento) (HU-35). Coincidencias por significado con embeddings de Gemini, evaluadas contra las de palabras con ejemplos del campus: cobertura 0.86 contra 0.19 (`SemanticEvaluationTests`, HU-60). IA: el acuerdo con IA desde el chat (HU-32/33/34, Gemini) y "Llenar con IA" desde la foto al publicar (HU-59, Gemini con la imagen) | HU-32 · HU-35 · HU-59 · HU-60 | ✅ |
| L-17 | Landing con enlace a la tienda y aviso de privacidad | `musubi-landing` en `https://musubiapp.com.mx`: qué es Musubi, cómo funciona, botón de Google Play, aviso de privacidad, términos, eliminar cuenta y la vista previa de un producto compartido (HU-61) | SCRUM-84 · SCRUM-110 | ✅ |
| L-18 | Una sola cuenta de GitHub | `Israel-Perez/musubi-api` y `Israel-Perez/musubi-app` | — | ✅ |
| L-19 | Evaluación el 02-Dic-2026; entrega en `.rar` + script de BD | Script de la base: `db/*.js` (idempotentes, en orden, `db/README.md`). Falta armar el `.rar` al cierre | 02-01 · equipo | 🟡 |

## 3. Cambios que provocó

- **SCRUM-66 (20-Oct-2026):** stack de datos PostgreSQL → MongoDB (L-14), fecha de evaluación 04-Dic → 02-Dic (L-19), matriz de versionamiento (L-11), maqueta versionada (L-04) y este checklist.
- **HU-32/33/34 (10-Nov-2026), decisión de la usuaria:** la IA generativa usa la **API de Gemini (capa gratuita)** y no un servicio de AWS. Choca con L-15c ("todas las funciones de la API en la nube elegida"): la API corre completa en AWS y solo la llamada al modelo sale a Google. Argumento para la evaluación: costo cero (L-15b), el proveedor va detrás de `IAgreementExtractor` y se cambia por configuración (L-15j, RNF-15) y, si falla, el chat y los pedidos siguen. Condiciones de la capa gratuita: Google puede usar lo que se envía para mejorar sus productos y revisarlo con personas, así que solo sale el chat de ese acuerdo, con los nombres cambiados por "Comprador" y "Vendedor" y sin correos, teléfonos, cuentas ni enlaces, y el aviso de privacidad lo dice (HU-47); la cuota es baja y cambia sin aviso (Flash-Lite, 6 resúmenes por persona por hora).
