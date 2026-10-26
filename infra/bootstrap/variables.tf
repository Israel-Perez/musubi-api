variable "region" {
  description = "Region de AWS (la misma que Atlas M0 y SES)."
  type        = string
  default     = "us-east-1"
}

variable "github_oidc_subject_prefix" {
  description = "Prefijo del claim sub de GitHub. El repo usa sujetos inmutables (owner@id/repo@id): gh api repos/<owner>/<repo>/actions/oidc/customization/sub."
  type        = string
  default     = "repo:Israel-Perez@234276594/musubi-api@1421377783"
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
