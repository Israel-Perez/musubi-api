variable "region" {
  description = "Region de AWS (la misma que Atlas M0 y SES)."
  type        = string
  default     = "us-east-1"
}

variable "image_tag" {
  description = "Tag de la imagen de la API en ECR (= <Version> del csproj, docs/versionado.md)."
  type        = string
}

variable "instance_type" {
  description = "Tipo de EC2. t3.micro: 2 vCPU, 1 GiB; suficiente para la API en el piloto."
  type        = string
  default     = "t3.micro"
}

variable "instance_count" {
  description = "Instancias de la API detras del balanceador (lineamiento 15: al menos 2)."
  type        = number
  default     = 2

  validation {
    condition     = var.instance_count >= 2
    error_message = "El lineamiento 15 exige al menos 2 instancias."
  }
}

# SCRUM-127 (lineamiento 15: backend escalable): el grupo crece por CPU hasta este tope y vuelve a instance_count.
variable "max_instance_count" {
  description = "Tope de instancias cuando el Auto Scaling Group crece por CPU."
  type        = number
  default     = 4

  validation {
    condition     = var.max_instance_count >= 2 && var.max_instance_count <= 6
    error_message = "El tope va de 2 a 6 instancias (costo: solo se pagan mientras hay carga)."
  }
}

variable "target_cpu_percent" {
  description = "CPU promedio del grupo que mantiene la politica de escalado (target tracking)."
  type        = number
  default     = 60

  validation {
    condition     = var.target_cpu_percent >= 20 && var.target_cpu_percent <= 90
    error_message = "La CPU objetivo va de 20 a 90 %."
  }
}

variable "ssm_prefix" {
  description = "Prefijo de Parameter Store con la configuracion secreta (cada parametro se vuelve una variable de entorno)."
  type        = string
  default     = "/musubi/prod"
}

variable "api_domain" {
  description = "Dominio propio de la API en CloudFront. Su certificado ACM (us-east-1, validado por DNS) debe estar emitido antes del apply; vacio = solo *.cloudfront.net."
  type        = string
  default     = "api.musubiapp.com.mx"
}

variable "log_retention_days" {
  description = "Dias que CloudWatch conserva los logs de la API."
  type        = number
  default     = 14
}

variable "alert_email" {
  description = "Correo que recibe las alarmas de CloudWatch (02-06). AWS pide confirmar la suscripcion."
  type        = string
  default     = "contacto@musubiapp.com.mx"
}

variable "redis_port" {
  description = "Puerto TCP de la base de Redis Cloud (parte del Public endpoint host:puerto). La cadena completa vive en /musubi/prod/Cache/ConnectionString."
  type        = number
  default     = 15092

  validation {
    condition     = var.redis_port >= 1024 && var.redis_port <= 65535
    error_message = "redis_port debe ser un puerto no privilegiado (1024 a 65535)."
  }
}
