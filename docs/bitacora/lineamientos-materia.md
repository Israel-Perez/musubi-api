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
| L-01 | Descarga desde Google Play | Prueba cerrada desde el 09-Nov-2026; producción antes del 02-Dic-2026 | app:02-02 | ⏳ |
| L-02 | Evaluación con prompts | Specs SDD, `docs/trazabilidad.md`, este checklist y `AGENTS.md` en cada repo | 00-02 | 🟡 |
| L-03 | Nube, BD, back, móvil y landing | `musubi-api`, `musubi-app`; Atlas (02-01); AWS (02-05); landing (repo propio) | 02-05 | 🟡 |
| L-04 | App igual a la maqueta, sin quitar funciones | Maqueta versionada en `musubi-app/docs/maqueta/` (60 pantallas) | app:02-01 | 🟡 |
| L-05 | SDD + DevSecOps | Constitución, plantilla, specs, critic, Semgrep, Dependabot | 00-01 | ✅ |
| L-06 | Desarrollo con agentes de IA | Claude Code (`.claude/`: agentes, skills y hooks) en lugar de OpenCode + WARP (sugerido, no obligatorio) | 00-02 | ✅ |
| L-07 | Pruebas unitarias, de seguridad y de integración (back y móvil) | Back: `UnitTest`, `SecurityTest`, `IntegrationTest`. App: unit + widget; seguridad e `integration_test` por completar | app:02-01 | 🟡 |
| L-08 | CI/CD back y móvil | CI en los dos repos; CD a AWS y a Google Play pendientes | 02-05 · app:02-02 | 🟡 |
| L-09 | Caché en BD, back y front | Redis/Valkey (cache-aside), caché de salida HTTP, caché local en la app | 02-02 · app:02-01 | ⏳ |
| L-10 | Logout en back y móvil | Revocación de la familia del refresh token (`RefreshTokenService.RevokeAsync`, 02-03) y borrado del almacenamiento seguro en la app | HU-02 | 🟡 |
| L-11 | Matriz de versionamiento antes del desarrollo, aplicada en cada despliegue | `docs/versionado.md` (sección compartida) + `VersioningTests` / `versioning_test.dart` | SCRUM-66 | ✅ (aplicación automática: 02-05 · app:02-02) |
| L-12 | MCP | MCP de Atlassian (Jira y Confluence) | — | ✅ |
| L-13a | Servicios en la nube; BD externa permitida | AWS (créditos del plan gratuito) + MongoDB Atlas M0 (`infra/`, parámetros en SSM) | 02-05 | 🟡 |
| L-13b | Load balancer | Application Load Balancer (`infra/stack/compute.tf`) | 02-05 | 🟡 |
| L-13c | IaC con Terraform (crear y eliminar) | `infra/bootstrap` + `infra/stack` | 02-05 | 🟡 |
| L-13d | Salida con SSL/HTTPS | CloudFront solo HTTPS (certificado `*.cloudfront.net`, HSTS); sin dominio, CloudFront → ALB va en HTTP dentro de AWS | 02-05 | 🟡 |
| L-14 | Base de datos NoSQL | MongoDB: driver, readiness y compose local (SCRUM-66); modelo e índices en 02-01 | 02-01 | 🟡 |
| L-15a | Al menos 2 instancias | Auto Scaling Group con 2 EC2 en 2 zonas (validación en Terraform: mínimo 2) | 02-05 | 🟡 |
| L-15b | AWS/Azure/GCP con costo cero | AWS (créditos de 100 USD, vencen el 20-Abr-2027) + Atlas M0 gratis; ~50 USD/mes encendido (`infra/README.md`) y alertas sin créditos | 02-05 | 🟡 |
| L-15c | Todas las funciones de la API en la nube | Monolito modular desplegado completo en AWS | 02-05 | ⏳ |
| L-15d | Endpoints eficientes | Índices (02-01), paginación y compresión (02-04) | 02-04 | ⏳ |
| L-15e | API o microservicios | Monolito modular (constitución §7) | 00-01 | ✅ |
| L-15f | Imágenes en el CDN | S3 privado + CloudFront (`/media/*`, OAC); la subida llega con HU-06 | 02-05 · HU-06 | 🟡 |
| L-15g | Observabilidad en la nube | CloudWatch Logs (eventos y errores) | 02-06 | ⏳ |
| L-15h | JWT | Access token HS256 de 15 min + refresh rotativo; sesión obligatoria por defecto (02-03; `SecurityTest/Auth/JwtTests.cs`) | HU-02 | 🟡 |
| L-15i | Caching, rate limit, middleware, paginación, índices, serializadores, compresión | Middleware y JSON camelCase listos (01-02); el resto en su spec | 02-02 · 02-04 | 🟡 |
| L-15j | Patrones de integración | Adaptadores detrás de interfaces (constitución §7) | Por HU | 🟡 |
| L-15k | Paso automatizado para crear/desplegar y para eliminar | Workflows `infra-up` / `infra-down` (OIDC, entorno `produccion`) | 02-05 | 🟡 |
| L-16a | Despliegue de GitHub a Google Play | Workflow con la API de Google Play | app:02-02 | ⏳ |
| L-16b | Moderación de texto, imágenes y video antes de guardar | Amazon Rekognition (imágenes) y Comprehend (texto) en el back antes de persistir | 02-07 · HU-06/13/20 | ⏳ |
| L-16c | Push en los procesos principales | FCM | HU-31 | ⏳ |
| L-16d | Aviso de privacidad en la app y en la landing | Pantallas de la maqueta + página de la landing | HU-47 | ⏳ |
| L-16e | Eliminar cuenta | Flujo de la maqueta | HU-46 | ⏳ |
| L-16f | Permisos del móvil | Cámara/galería y notificaciones, pedidos en contexto | app:02-01 | ⏳ |
| L-16g | Sesión siempre viva salvo logout | Refresh token rotativo de 60 días que se desliza con cada uso (02-03); en la app, almacenamiento seguro e interceptor | HU-02 · app:02-01 | 🟡 |
| L-16h | SQLite + sincronización (si aplica) | Por decidir en app:02-01 (borradores y caché de consulta) | app:02-01 | ⏳ |
| L-16i | Al menos una función de ML y una de IA generativa | ML: recomendaciones (HU-35). IA: resumen del acuerdo (HU-32/33/34) | HU-32 · HU-35 | ⏳ |
| L-17 | Landing con enlace a la tienda y aviso de privacidad | Repo propio de la landing | Por crear | ⏳ |
| L-18 | Una sola cuenta de GitHub | `Israel-Perez/musubi-api` y `Israel-Perez/musubi-app` | — | ✅ |
| L-19 | Evaluación el 02-Dic-2026; entrega en `.rar` + script de BD | Script `mongosh` (02-01); paquete `.rar` al cierre | 02-01 | ⏳ |

## 3. Cambios que provocó

- **SCRUM-66 (20-Oct-2026):** stack de datos PostgreSQL → MongoDB (L-14), fecha de evaluación 04-Dic → 02-Dic (L-19), matriz de versionamiento (L-11), maqueta versionada (L-04) y este checklist.
