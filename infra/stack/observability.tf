# 02-06: alarmas con aviso por correo y dashboard de CloudWatch. Dentro de la capa gratuita de
# CloudWatch (10 alarmas, 3 dashboards) y de SNS (1,000 correos al mes).

resource "aws_sns_topic" "alerts" { # nosemgrep: terraform.aws.security.aws-sns-topic-unencrypted.aws-sns-topic-unencrypted
  name = "musubi-alertas"
}

# AWS manda un correo de confirmacion: la suscripcion queda pendiente hasta aceptarlo.
resource "aws_sns_topic_subscription" "email" {
  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

locals {
  lb_dimension = { LoadBalancer = aws_lb.api.arn_suffix }
  tg_dimensions = {
    LoadBalancer = aws_lb.api.arn_suffix
    TargetGroup  = aws_lb_target_group.api.arn_suffix
  }
}

# Errores de la API: lineas JSON con LogLevel Error o Critical (logs estructurados, 02-06).
resource "aws_cloudwatch_log_metric_filter" "api_errors" {
  name           = "musubi-api-errores"
  log_group_name = aws_cloudwatch_log_group.api.name
  pattern        = "{ ($.LogLevel = \"Error\") || ($.LogLevel = \"Critical\") }"

  metric_transformation {
    name          = "ApiErrors"
    namespace     = "Musubi"
    value         = "1"
    default_value = "0"
  }
}

resource "aws_cloudwatch_metric_alarm" "api_errors" {
  alarm_name          = "musubi-api-errores"
  alarm_description   = "La API registro 5 o mas errores en 5 minutos. Busca el TraceId en CloudWatch Logs."
  namespace           = "Musubi"
  metric_name         = "ApiErrors"
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 5
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "target_5xx" {
  alarm_name          = "musubi-api-5xx"
  alarm_description   = "Las instancias respondieron 5 o mas errores 5xx en 5 minutos."
  namespace           = "AWS/ApplicationELB"
  metric_name         = "HTTPCode_Target_5XX_Count"
  dimensions          = local.lb_dimension
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 5
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "healthy_hosts" {
  alarm_name          = "musubi-api-instancias-sanas"
  alarm_description   = "Hay menos de ${var.instance_count} instancias sanas detras del balanceador durante 10 minutos."
  namespace           = "AWS/ApplicationELB"
  metric_name         = "HealthyHostCount"
  dimensions          = local.tg_dimensions
  statistic           = "Minimum"
  period              = 300
  evaluation_periods  = 2
  threshold           = var.instance_count
  comparison_operator = "LessThanThreshold"
  treat_missing_data  = "breaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "latency_p95" {
  alarm_name          = "musubi-api-latencia-p95"
  alarm_description   = "El p95 de la latencia de la API paso de 1.5 s durante 15 minutos (RNF de 00-03)."
  namespace           = "AWS/ApplicationELB"
  metric_name         = "TargetResponseTime"
  dimensions          = local.lb_dimension
  extended_statistic  = "p95"
  period              = 300
  evaluation_periods  = 3
  threshold           = 1.5
  comparison_operator = "GreaterThanThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]
}

resource "aws_cloudwatch_dashboard" "api" {
  dashboard_name = "musubi-api"
  dashboard_body = jsonencode({
    widgets = [
      {
        type = "metric", x = 0, y = 0, width = 12, height = 6
        properties = {
          title  = "Peticiones y errores 5xx"
          region = var.region
          stat   = "Sum"
          period = 300
          metrics = [
            ["AWS/ApplicationELB", "RequestCount", "LoadBalancer", aws_lb.api.arn_suffix],
            ["AWS/ApplicationELB", "HTTPCode_Target_5XX_Count", "LoadBalancer", aws_lb.api.arn_suffix],
            ["AWS/ApplicationELB", "HTTPCode_Target_4XX_Count", "LoadBalancer", aws_lb.api.arn_suffix],
          ]
        }
      },
      {
        type = "metric", x = 12, y = 0, width = 12, height = 6
        properties = {
          title  = "Latencia (p50 y p95, segundos)"
          region = var.region
          period = 300
          metrics = [
            ["AWS/ApplicationELB", "TargetResponseTime", "LoadBalancer", aws_lb.api.arn_suffix, { stat = "p50" }],
            ["AWS/ApplicationELB", "TargetResponseTime", "LoadBalancer", aws_lb.api.arn_suffix, { stat = "p95" }],
          ]
        }
      },
      {
        type = "metric", x = 0, y = 6, width = 12, height = 6
        properties = {
          title   = "Instancias sanas"
          region  = var.region
          stat    = "Minimum"
          period  = 60
          metrics = [["AWS/ApplicationELB", "HealthyHostCount", "TargetGroup", aws_lb_target_group.api.arn_suffix, "LoadBalancer", aws_lb.api.arn_suffix]]
        }
      },
      {
        type = "metric", x = 12, y = 6, width = 12, height = 6
        properties = {
          title   = "Errores registrados por la API"
          region  = var.region
          stat    = "Sum"
          period  = 300
          metrics = [["Musubi", "ApiErrors"]]
        }
      },
      {
        type = "log", x = 0, y = 12, width = 24, height = 8
        properties = {
          title  = "Ultimos errores (con TraceId)"
          region = var.region
          query  = "SOURCE '${aws_cloudwatch_log_group.api.name}' | fields @timestamp, Category, Message, Scopes.0.TraceId | filter LogLevel = 'Error' or LogLevel = 'Critical' | sort @timestamp desc | limit 50"
          view   = "table"
        }
      },
    ]
  })
}
