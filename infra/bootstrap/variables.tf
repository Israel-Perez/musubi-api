variable "region" {
  description = "Region de AWS (la misma que Atlas M0 y SES)."
  type        = string
  default     = "us-east-1"
}

variable "github_repository" {
  description = "Repositorio que puede asumir el rol de despliegue (owner/nombre)."
  type        = string
  default     = "Israel-Perez/musubi-api"
}

variable "github_environment" {
  description = "Entorno de GitHub (restringido a main) cuyos jobs pueden asumir el rol."
  type        = string
  default     = "produccion"
}

variable "alert_email" {
  description = "Correo que recibe las alertas de gasto. No se versiona: -var o terraform.tfvars (ignorado)."
  type        = string
}
