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
