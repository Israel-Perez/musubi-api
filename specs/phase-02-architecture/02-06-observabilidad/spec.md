# 02-06 — Observabilidad y seguridad en el pipeline

**HU / Jira / Sprint / Módulo / Contraparte:** N/A (spec técnica) / SCRUM-86 / S3–S4 / Plataforma / musubi-app: Crashlytics (`NOTE (HU-31)`)

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
- **Logs:** el logger JSON de .NET (`AddJsonConsole`) con scopes y un filtro que redacta campos sensibles. El `traceId` sale del `Activity` actual.
- **Infraestructura:** `infra/stack/observability.tf` con alarmas, tema SNS, dashboard y filtros de métricas.
- **CI:** jobs `trivy` y `zap` (este último contra el ambiente desplegado, en `infra-up`). Las versiones se fijan por SHA.

## Contratos
- Configuración: `Logging:Console:FormatterName=json` (`01-04`).
- Terraform: variable `alarm_email`.

## Tests
- `UnitTest/Platform/`: el redactor quita el correo, el token y la contraseña de un log de ejemplo.
- `IntegrationTest/`: un error devuelve un `traceId` que también aparece en el log capturado.
- `terraform validate` y `plan` sin destrucciones.

## Criterios
- [ ] En CloudWatch Logs Insights se encuentra una petición por su `traceId`.
- [ ] Una alarma de prueba llega por correo.
- [ ] Trivy y ZAP corren y sus resultados quedan como artefacto.
- [ ] Ningún log de prueba contiene datos personales.

## Límites
- **Crashlytics** (errores de la app) es de `musubi-app` (HU-31).
- **Decisión pendiente:** SonarCloud (necesita `SONAR_TOKEN` y una organización) o una alternativa.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 29-Oct-2026
- **Detalle:** spec técnica creada con el backlog en borrador (SCRUM-86). Cubre L-15g y la parte de DevSecOps de L-05.
