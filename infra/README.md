# infra — Terraform de MUSUBI en AWS (spec 02-05)

| Carpeta | Qué crea | Cuándo se aplica |
|---|---|---|
| `bootstrap/` | ECR `musubi-api`, proveedor OIDC de GitHub, rol `musubi-github-deploy`, presupuesto con alertas | **Una vez**, desde la PC de un administrador |
| `stack/` | Red (2 zonas), ALB, Auto Scaling Group (2 EC2), CloudFront, S3 de imágenes, IAM de las instancias, CloudWatch | Workflow `infra-up` (y `infra-down` para eliminar) |

`VERSION` es la versión de la infraestructura (tag `infra-vX.Y.Z`, `docs/versionado.md`).

## Primera vez (administrador)

```powershell
aws login                                    # usuario IAM con MFA (no Identity Center: el plan gratuito perdería los créditos)
./scripts/bootstrap-state.ps1                # bucket musubi-tfstate-<cuenta>
cd infra/bootstrap
terraform init -backend-config="bucket=musubi-tfstate-<cuenta>" -backend-config="key=bootstrap/terraform.tfstate" -backend-config="region=us-east-1" -backend-config="use_lockfile=true"
terraform apply -var "alert_email=<correo para alertas>"
```

Después, en GitHub (entorno `produccion`, solo rama `main`): variable `AWS_DEPLOY_ROLE_ARN` = salida `github_deploy_role_arn`.

## Secretos (Parameter Store, nunca en el repo)

Cada parámetro `/musubi/prod/A/B` llega a la API como la variable `A__B` (= `A:B` en .NET).

| Parámetro | Tipo | Qué es |
|---|---|---|
| `/musubi/prod/ConnectionStrings/Mongo` | SecureString | Cadena `mongodb+srv://` de Atlas |
| `/musubi/prod/Jwt/SigningKey` | SecureString | Llave HS256 (base64, 64 bytes) |
| `/musubi/prod/Email/From` | String | Remitente verificado en SES |

Para guardar un secreto sin que quede en el historial de la terminal:

```powershell
$s = Read-Host "Valor" -AsSecureString
aws ssm put-parameter --name "/musubi/prod/<Clave>" --type SecureString --overwrite --value ([Net.NetworkCredential]::new('', $s).Password)
```

Después de cambiar un secreto hay que reemplazar las instancias: correr `infra-up` otra vez, o iniciar un *instance refresh* del grupo `musubi-api`.

## Dominio (`musubiapp.com.mx`, DNS en Cloudflare)

| Nombre | Para qué | Dónde se configura |
|---|---|---|
| `musubiapp.com.mx` y `www` | Sitio público con avisos legales (`musubi-landing`); `www` redirige a la raíz | Cloudflare Workers (dominios personalizados) |
| `api.musubiapp.com.mx` | La API (`API_BASE_URL` de la app). Es fija aunque se recree la infraestructura | CNAME en Cloudflare → salida `cloudfront_domain`, **DNS only** (nube gris) |
| `contacto@musubiapp.com.mx` | Contacto del aviso de privacidad y de Google Play | Cloudflare Email Routing → reenvío a un buzón del equipo |
| `<token>._domainkey` (3), MX y TXT de `mail`, TXT `_dmarc` | Correo de SES con DKIM, SPF y DMARC | DNS de Cloudflare |

**Certificado de la API.** Se pide **una vez**, fuera de Terraform, porque su validación es un registro en Cloudflare. ACM lo renueva solo mientras el CNAME de validación exista. Terraform lo busca por nombre (`data.aws_acm_certificate`), y el apply falla si no está emitido:

```powershell
aws acm request-certificate --region us-east-1 --domain-name api.musubiapp.com.mx --validation-method DNS --key-algorithm EC_prime256v1
aws acm describe-certificate --region us-east-1 --certificate-arn <arn> --query "Certificate.DomainValidationOptions[0].ResourceRecord"
# Ese CNAME va en Cloudflare (DNS only). Espera a que el estado sea ISSUED antes de correr infra-up.
```

**Si se recrea la distribución** (`infra-down` + `infra-up`), actualiza el CNAME de `api` en Cloudflare con la nueva salida `cloudfront_domain`. La app no cambia.

## Correo (SES)

- **Remitente:** `Musubi <no-reply@musubiapp.com.mx>`, en `/musubi/prod/Email/From`. El dominio está verificado con DKIM y MAIL FROM `mail.musubiapp.com.mx`. Cambiar el remitente requiere reemplazar las instancias:

  ```powershell
  aws autoscaling start-instance-refresh --auto-scaling-group-name <asg> --preferences '{"MinHealthyPercentage":50,"InstanceWarmup":180}'
  ```

- **Sandbox:** mientras AWS no apruebe el acceso de producción, SES solo envía a direcciones verificadas. Para agregar un correo de prueba (le llega un enlace de confirmación):

  ```powershell
  aws sesv2 create-email-identity --email-identity "<correo>"
  ```

## Costo estimado (us-east-1, encendido 24/7)

| Recurso | USD/mes |
|---|---|
| ALB (horas + LCU mínimas) | ~18 |
| 2 × `t3.micro` (créditos `unlimited`; sin cargo extra si el promedio de CPU queda bajo el 10 %) | ~15 |
| IPv4 públicas (2 instancias + ALB) | ~15 |
| 2 discos gp3 de 10 GB | ~2 |
| CloudFront, S3, CloudWatch, Parameter Store, ECR | ~0–1 (capas gratuitas) |
| **Total** | **~50** |

Con 100 USD de créditos conviene encenderlo solo en las ventanas de prueba y evaluación, y apagarlo con `infra-down`. Al volver a crear la infraestructura cambia la URL de CloudFront: hay que actualizar el CNAME de `api` en Cloudflare (sección "Dominio").
