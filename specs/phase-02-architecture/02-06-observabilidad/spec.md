# 02-06 — Observabilidad y seguridad en el pipeline

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-93 (antes SCRUM-86) / S3–S4 / Plataforma / musubi-app: Crashlytics (`NOTE (HU-31)`)

## Contexto
Los lineamientos piden observabilidad en la nube (L-15g).

Hoy:
- los logs del contenedor llegan a CloudWatch (`/musubi/prod/api`, 02-05) en texto plano;
- no hay métricas propias, alarmas ni correlación por petición;
- no se ejecutan en el CI Trivy (escaneo de la imagen), ZAP (DAST) ni SonarCloud (AGENTS §6).

**Depende de:** `02-03` (`traceId` en los errores) y `02-05` (CloudWatch y ALB).

## Requisitos
1. **Logs estructurados en JSON:**
   - nivel, mensaje, `traceId`, ruta, código y duración;
   - **sin datos personales**: nada de correos, tokens, contraseñas ni contenido de mensajes (RNF-05).
2. **Correlación:** el `traceId` de cada petición aparece en sus logs y en el `problem+json` que recibe la app.
3. **Métricas:** del ALB (5xx, latencia, hosts sanos) y propias de la API, con Embedded Metric Format:
   - registros;
   - publicaciones;
   - errores de proveedores externos.
4. **Alarmas con aviso por correo** a `contacto@musubiapp.com.mx`:
   - 5xx sostenidos;
   - menos de 2 hosts sanos;
   - latencia p95 alta;
   - gasto por encima del presupuesto (ya existe en 02-05).
5. **Dashboard de CloudWatch** con lo anterior, definido en Terraform.
6. **Seguridad en el pipeline:**
   - Trivy sobre la imagen en el CI (falla con vulnerabilidades críticas);
   - ZAP baseline contra la API desplegada;
   - análisis de calidad (SonarCloud o equivalente gratuito).

## Diseño
- **Logs (`Platform/Observability/ObservabilityExtensions.cs`):**
  - fuera de Development, `AddJsonConsole` (scopes y hora UTC);
  - `ActivityTrackingOptions` agrega `TraceId` y `SpanId`;
  - `UseHttpLogging` deja una línea por petición (método, ruta, código y duración), sin `/health*`;
  - nunca se registran la query, los encabezados ni los cuerpos.
- **Niveles en producción:** `Musubi` en `Information`, así llegan a CloudWatch los eventos del negocio (cuenta eliminada, moderación). El resto queda en `Warning`.
- **`infra/stack/observability.tf`:**
  - tema SNS `musubi-alertas` con suscripción por correo (`alert_email`);
  - filtro de métricas sobre los logs JSON (`LogLevel` Error o Critical → `Musubi/ApiErrors`);
  - 4 alarmas: errores de la API, 5xx, instancias sanas y latencia p95;
  - dashboard `musubi-api`.
- **CI:** Trivy `0.76.1` (fijado por digest) escanea la imagen en `docker-build` y falla con vulnerabilidades críticas que ya tienen arreglo.

## Contratos
- **Configuración:** `Logging:LogLevel:Musubi` y `Logging:LogLevel:Microsoft.AspNetCore.HttpLogging` (`appsettings.Production.json`); variable de Terraform `alert_email`.
- **Sin rutas nuevas.** El `traceId` del `problem+json` (02-03) es el mismo de los logs.

## Tests
- **`SecurityTest/Observability/RequestLoggingTests.cs`:** una petición deja su línea con ruta y código, sin query, sin token y sin `/health`. Prueba de mordida: con `RequestQuery` en los campos, falla.
- **Infraestructura:** `terraform validate` (job `terraform` del CI).
- **Trivy:** su propio job (`docker-build`).

## Criterios
- [x] Los logs de producción son JSON con `TraceId` y sin datos personales (`RequestLoggingTests`).
- [x] Alarmas y dashboard en Terraform (`terraform validate`).
- [ ] Después de `infra-up`: el correo de suscripción confirmado y el dashboard con datos.
- [x] Trivy en el CI.

## Límites
- **`NOTE (02-06)`:**
  - métricas propias con Embedded Metric Format (registros y publicaciones);
  - ZAP baseline contra la API desplegada;
  - SonarCloud. Requiere el secreto `SONAR_TOKEN` y la organización.
- **El gasto** ya lo vigila el presupuesto de `infra/bootstrap` (02-05).

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026 (borrador) · 03-Nov-2026 (implementación)
- **Detalle:**
  - logs, alarmas, dashboard y Trivy (SCRUM-93);
  - EMF, ZAP y SonarCloud quedan pendientes.
