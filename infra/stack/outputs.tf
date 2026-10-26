output "api_url" {
  description = "URL publica de la API (HTTPS). Va en API_BASE_URL de musubi-app (config/prod.json)."
  value       = "https://${aws_cloudfront_distribution.main.domain_name}"
}

output "alb_dns_name" {
  description = "Balanceador (solo acepta trafico que llega por CloudFront)."
  value       = aws_lb.api.dns_name
}

output "autoscaling_group" {
  description = "Grupo de las instancias de la API."
  value       = aws_autoscaling_group.api.name
}

output "log_group" {
  description = "Logs de la API en CloudWatch."
  value       = aws_cloudwatch_log_group.api.name
}

output "media_bucket" {
  description = "Bucket privado de imagenes (servido por CloudFront en /media/*)."
  value       = aws_s3_bucket.media.bucket
}
