# plan.md — 02-05 Despliegue en AWS

1. **Componentes:** `scripts/bootstrap-state.ps1`, `infra/bootstrap/`, `infra/stack/`, `.github/workflows/infra-up.yml` e `infra-down.yml`, el job `terraform` del CI y `CloudFrontForwardedHeaders`.
2. **Decisiones:**
   - **EC2 + Docker y no ECS/Fargate:** el lineamiento pide instancias y balanceador. Además, 2 `t3.micro` cuestan menos que 2 tareas Fargate siempre encendidas.
   - **CloudFront delante del ALB:** da HTTPS sin dominio ni certificado propio y es también el CDN de imágenes.
   - **Cabeceras de CloudFront para la IP real:** el `X-Forwarded-For` que arma el ALB termina en la IP de CloudFront, así que la IP del cliente se toma de `CloudFront-Viewer-Address`.
   - **ECR y no GHCR:** imagen privada, el rol de la instancia la descarga sin credenciales y el costo es casi cero.
   - **Parameter Store y no Secrets Manager:** el nivel estándar es gratis; Secrets Manager cuesta 0.40 USD por secreto al mes.
   - **Sin NAT Gateway:** costaría ~32 USD al mes. Las instancias tienen IP pública, pero su grupo de seguridad solo acepta tráfico del ALB.
   - **Créditos de CPU `unlimited`** (el modo por defecto): en `standard` una `t3` arranca sin créditos y queda al 10 % de CPU. Solo hay cargo si el promedio de 24 horas pasa del 10 %.
3. **Guardarraíles:**
   - el rol OIDC solo confía en el entorno `produccion`, restringido a `main`;
   - tags de imagen inmutables y versión obligatoria;
   - estado de Terraform versionado y cifrado;
   - alertas de gasto sin contar créditos.
4. **Validación:** `terraform validate`, actionlint, SecurityTest y la prueba de humo de `infra-up`.
5. **Secuencia:** bucket del estado → `infra/bootstrap` → parámetros SSM → entorno de GitHub → PR → `infra-up` → configuración de la app.
