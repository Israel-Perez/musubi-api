output "ecr_repository_url" {
  description = "Registro de la imagen de la API."
  value       = aws_ecr_repository.api.repository_url
}

output "github_deploy_role_arn" {
  description = "Rol que asume GitHub Actions (variable AWS_DEPLOY_ROLE_ARN del entorno produccion)."
  value       = aws_iam_role.github_deploy.arn
}
