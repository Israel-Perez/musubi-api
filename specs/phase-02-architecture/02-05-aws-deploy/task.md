# 02-05 — Despliegue en AWS (tareas)

## T1 — Base de la cuenta
- **Crear:** `scripts/bootstrap-state.ps1` y `infra/bootstrap/`; parámetros `/musubi/prod/*`; identidad de SES; entorno `produccion` de GitHub con `AWS_DEPLOY_ROLE_ARN`.
- **Verificar:** `terraform plan` (6 recursos) y `aws ssm describe-parameters`.
- **Estado:** 🟡 Bucket del estado, parámetros y entorno de GitHub listos; el `apply` de `infra/bootstrap` lo autoriza el administrador.

## T2 — Infraestructura y despliegue
- **Crear:** `infra/stack/`, `infra-up.yml`, `infra-down.yml`, el job `terraform` del CI y `CloudFrontForwardedHeaders` con sus pruebas.
- **Verificar:** `terraform validate`, actionlint, `SecurityTest/Hosting/CloudFrontHeadersTests.cs` y la prueba de humo de `infra-up`.
- **Estado:** 🟡 Implementado; falta el primer `infra-up` después del merge.

## Pendientes
- `NOTE (02-05)`: dominio propio (certificado en el ALB, URL estable y SES fuera del sandbox).
- `NOTE (02-06)`: métricas, alarmas y logs estructurados en CloudWatch.
- `NOTE (02-02)`: Redis/Valkey para la caché de nivel base de datos.
