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
| `/musubi/prod/Cache/ConnectionString` | SecureString | Redis Cloud gratuito (02-02 y HU-20): caché compartida y backplane del chat en tiempo real. Formato de StackExchange.Redis: `host:puerto,user=default,password=...,abortConnect=false` (+ `,ssl=true` con TLS). **No** el comando `redis-cli -u redis://...` que muestra Redis Cloud. El puerto debe coincidir con la variable `redis_port` de Terraform (salida del grupo de seguridad). Sin el parámetro, cada instancia usa su memoria y el chat solo entrega en tiempo real dentro de su instancia |

Para guardar un secreto sin que quede en el historial de la terminal:

```powershell
$s = Read-Host "Valor" -AsSecureString
aws ssm put-parameter --name "/musubi/prod/<Clave>" --type SecureString --overwrite --value ([Net.NetworkCredential]::new('', $s).Password)
```

Después de cambiar un secreto hay que reemplazar las instancias: correr `infra-up` otra vez, o iniciar un *instance refresh* del grupo `musubi-api`.

## Moderación (02-07)

Cada foto subida pasa por Rekognition (`DetectModerationLabels`) y cada texto publicado por Translate (español → inglés) y Comprehend (`DetectToxicContent`, que solo acepta inglés). El rol de las instancias tiene los tres permisos (`Moderar` en `stack/iam.tf`).

Costo del piloto: ~1 USD/mes con 500 fotos y 500 publicaciones. Precios de us-east-1:
- Rekognition: 0.001 USD por imagen;
- Translate: 15 USD por millón de caracteres;
- Comprehend: 0.0001 USD por unidad de 100 caracteres.

## Observabilidad (02-06)

- **Logs:** el contenedor escribe JSON a la consola y el driver `awslogs` los manda a `/musubi/prod/api`. Cada línea lleva `TraceId`, el mismo que recibe la app en el `problem+json` de un error.
- **Dashboard** `musubi-api` (CloudWatch → Dashboards): peticiones, 5xx, latencia p50/p95, instancias sanas, errores de la API y los últimos errores con su `TraceId`.
- **Alarmas** (aviso por correo a `alert_email`, por defecto `contacto@musubiapp.com.mx`):
  - 5 o más errores en 5 minutos;
  - 5 o más respuestas 5xx en 5 minutos;
  - menos instancias sanas que `instance_count` durante 10 minutos;
  - latencia p95 mayor a 1.5 s durante 15 minutos.
- **Confirmar el correo:** después del primer `infra-up` con 02-06, AWS manda un correo "AWS Notification - Subscription Confirmation". Hay que abrirlo y confirmar; si no, las alarmas no avisan.
- **Costo:** dentro de la capa gratuita (10 alarmas, 3 dashboards y 1,000 correos de SNS al mes).

## Dominio (`musubiapp.com.mx`, DNS en Cloudflare)

| Nombre | Para qué | Dónde se configura |
|---|---|---|
| `musubiapp.com.mx` y `www` | Sitio público con avisos legales (`musubi-landing`); `www` redirige a la raíz | Cloudflare Workers (dominios personalizados) |
| `api.musubiapp.com.mx` | La API (`API_BASE_URL` de la app). Es fija aunque se recree la infraestructura | CNAME en Cloudflare → salida `cloudfront_domain`, **DNS only** (nube gris) |
| `contacto@musubiapp.com.mx` | Contacto del aviso de privacidad y de Google Play | Cloudflare Email Routing → reenvío a un buzón del equipo |
| `<token>._domainkey` (3), MX y TXT de `mail`, TXT `_dmarc` | Correo de SES con DKIM, SPF y DMARC | DNS de Cloudflare |

**Certificado de la API.** Se pide **una vez**, fuera de Terraform, porque su validación es un registro en Cloudflare. ACM lo renueva solo mientras el CNAME de validación exista. Terraform lo busca por nombre y tipo de llave (`data.aws_acm_certificate`, `key_types = ["EC_prime256v1"]`), y el apply falla si no está emitido:

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
