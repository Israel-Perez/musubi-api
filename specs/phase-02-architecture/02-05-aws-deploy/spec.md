# 02-05 — Despliegue en AWS

**HU / Jira / Sprint / Módulo / Contraparte:** — / SCRUM-80 / S2 / Plataforma / [musubi-app:02-02](https://github.com/Israel-Perez/musubi-app/tree/main/specs) (config `prod`)

## Contexto
Lineamientos 13 y 15 ([checklist](../../../docs/bitacora/lineamientos-materia.md)): la API corre en AWS con HTTPS, al menos 2 instancias detrás de un balanceador, infraestructura como código, costo cero (créditos del plan gratuito) y un paso automatizado para crear y otro para eliminar. Base de datos externa: MongoDB Atlas M0. **Depende de**: `02-01` (esquema `db/`), `02-03` (secretos JWT y correo).

## Requisitos
1. **Cómputo:** Application Load Balancer y un Auto Scaling Group con **2 EC2** (`t3.micro`, créditos `standard`) en 2 zonas. La revisión de salud del balanceador es `/health/ready`.
2. **HTTPS sin dominio propio:** CloudFront con su certificado `*.cloudfront.net` (solo HTTPS, `SecurityHeadersPolicy` con HSTS).
   - El balanceador solo acepta tráfico de CloudFront: prefix list `origin-facing` y la cabecera secreta `X-Origin-Verify`; sin ella responde 403.
   - Las instancias solo aceptan tráfico del balanceador.
3. **IP real del cliente:** con `Proxy:TrustCloudFrontHeaders=true`, la API toma la IP de `CloudFront-Viewer-Address` y el esquema de `CloudFront-Forwarded-Proto`. Así el rate limit es por estudiante y no global. Sin esa clave, las cabeceras se ignoran.
4. **Secretos:** en Parameter Store, bajo `/musubi/prod/*`. Cada `A/B` se vuelve la variable `A__B` al arrancar la instancia. Nunca en el repo, en la imagen ni en Terraform.
5. **Imágenes (CDN):** bucket S3 privado servido por CloudFront en `/media/*` con OAC.
6. **Observabilidad mínima:** los logs del contenedor van a CloudWatch (`/musubi/prod/api`, 14 días). El resto llega con 02-06.
7. **Automatización (lineamiento 15):**
   - `infra-up`: versión → imagen en ECR → `db/*.js` en Atlas → `terraform apply` → espera del reemplazo → prueba de humo → tags `api-vX.Y.Z` e `infra-vX.Y.Z`.
   - `infra-down`: `terraform destroy`, con la confirmación `DESTRUIR`.
8. **Versión (lineamiento 11):** si el tag `api-vX.Y.Z` ya existe en otro commit, falla; la imagen se publica con tag inmutable.
9. **Costo:** sin NAT Gateway, sin base administrada de AWS, sin dominio. Alertas de gasto a 10, 25 y 50 USD sin contar créditos.

## Diseño
- `infra/bootstrap/` (se aplica una vez): ECR, proveedor OIDC de GitHub, rol `musubi-github-deploy` y presupuesto. Detalle en [`infra/README.md`](../../../infra/README.md).
  - El rol confía solo en `repo:Israel-Perez/musubi-api:environment:produccion`.
  - El entorno `produccion` de GitHub solo acepta `main`.
- `scripts/bootstrap-state.ps1`: bucket del estado (`musubi-tfstate-<cuenta>`, versionado y cifrado; bloqueo nativo de S3 con `use_lockfile`).
- `infra/stack/`: red, ALB, ASG, CloudFront, S3 de imágenes, IAM de las instancias y CloudWatch. `user-data.sh.tftpl` instala Docker, lee los parámetros y arranca la imagen.
- `Musubi.Api/Platform/Hosting/CloudFrontForwardedHeaders.cs`, después de `UseForwardedHeaders` (pipeline 01-02).

## Contratos
Sin endpoints nuevos. La URL pública es la salida `api_url` de Terraform, que va en `API_BASE_URL` de `musubi-app` (`config/prod.json`).

## Tests
- `SecurityTest/Hosting/CloudFrontHeadersTests.cs` (11):
  - detrás de CloudFront cada cliente tiene su propio límite (IPv4 e IPv6);
  - sin la clave de configuración la cabecera se ignora (no se puede falsificar);
  - lectura de la IP y del esquema.
- CI: job `terraform` (`fmt -check` + `validate` de los dos directorios) y `actionlint` de los workflows.
- Self-test (25-Oct-2026): si el middleware no se registra, falla `BehindCloudFront_EachViewerHasItsOwnRateLimit`.
- Prueba de humo de `infra-up`: `/health/ready` y `/api/v1/ping` en 200 por CloudFront.

## Criterios
- Build Release 0/0 · Unit 91 · Integration 46 · Security 39 · Contract 5 · `terraform validate` OK · actionlint OK.
- Despliegue: `infra-up` en verde y `https://<distribución>.cloudfront.net/health/ready` → 200 con 2 instancias sanas.

## Límites
- **Sin dominio propio:**
  - el tramo CloudFront → ALB va en HTTP dentro de AWS (protegido por la prefix list y la cabecera secreta);
  - la URL cambia si se destruye y se vuelve a crear la infraestructura;
  - SES queda en sandbox: solo envía a direcciones verificadas (`aws sesv2 create-email-identity`). `NOTE (02-05)`: dominio propio, certificado ACM en el ALB y acceso de producción de SES.
- El rol de GitHub tiene `AdministratorAccess` (Terraform crea IAM, VPC y CloudFront). La protección está en la confianza (un repo, un entorno, solo `main`).
- `infra-up` se lanza a mano. Un despliegue automático en cada push a `main` necesita subir `<Version>` en cada PR.

## Aprobación y Control de Cambios
- **Estado:** 🚧 Borrador con evidencia
- **Revisores:** Pendiente de designar (1 integrante distinto del autor)
- **Fecha:** 25-Oct-2026
- **Detalle:** Sin dominio propio por decisión del usuario (25-Oct-2026).
