# Balanceador + Auto Scaling Group con 2 EC2 en 2 zonas (lineamientos 13 y 15).

data "aws_caller_identity" "current" {}

data "aws_ecr_repository" "api" {
  name = "musubi-api"
}

# Amazon Linux 2023 mas reciente (trae AWS CLI; Docker se instala al arrancar).
data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

# Cabecera secreta que solo CloudFront conoce: el balanceador rechaza lo que no la traiga.
resource "random_password" "origin_secret" {
  length  = 40
  special = false
}

# Logs de acceso del ALB: NOTE (02-06). Hoy los logs de la API van a CloudWatch.
resource "aws_lb" "api" { # nosemgrep: terraform.aws.security.aws-elb-access-logs-not-enabled.aws-elb-access-logs-not-enabled
  name                       = "musubi-api"
  load_balancer_type         = "application"
  internal                   = false
  security_groups            = [aws_security_group.alb.id]
  subnets                    = aws_subnet.public[*].id
  drop_invalid_header_fields = true
  idle_timeout               = 60
}

resource "aws_lb_target_group" "api" {
  name                 = "musubi-api"
  port                 = 8080
  protocol             = "HTTP"
  target_type          = "instance"
  vpc_id               = aws_vpc.main.id
  deregistration_delay = 30

  # Readiness (01-03): base alcanzable y esquema al dia; si no, la instancia no recibe trafico.
  health_check {
    path                = "/health/ready"
    matcher             = "200"
    interval            = 15
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.api.arn
  port              = 80
  protocol          = "HTTP" # nosemgrep: terraform.aws.security.insecure-load-balancer-tls-version.insecure-load-balancer-tls-version -- sin dominio no hay certificado; solo entra CloudFront con la cabecera secreta

  default_action {
    type = "fixed-response"

    fixed_response {
      content_type = "text/plain"
      message_body = "Forbidden"
      status_code  = "403"
    }
  }
}

resource "aws_lb_listener_rule" "from_cloudfront" {
  listener_arn = aws_lb_listener.http.arn
  priority     = 1

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }

  condition {
    http_header {
      http_header_name = "X-Origin-Verify"
      values           = [random_password.origin_secret.result]
    }
  }
}

# Cifrado por defecto con llave de AWS; una llave KMS propia cuesta ~1 USD/mes (costo cero).
resource "aws_cloudwatch_log_group" "api" { # nosemgrep: terraform.aws.security.aws-cloudwatch-log-group-unencrypted.aws-cloudwatch-log-group-unencrypted
  name              = "/musubi/prod/api"
  retention_in_days = var.log_retention_days
}

locals {
  image = "${data.aws_ecr_repository.api.repository_url}:${var.image_tag}"
}

resource "aws_launch_template" "api" {
  name_prefix   = "musubi-api-"
  image_id      = nonsensitive(data.aws_ssm_parameter.al2023.value)
  instance_type = var.instance_type

  iam_instance_profile {
    arn = aws_iam_instance_profile.api.arn
  }

  vpc_security_group_ids = [aws_security_group.api.id]

  # "unlimited" (el modo por defecto de t3): en "standard" una t3 arranca SIN creditos y queda al 10 % de CPU
  # mientras instala Docker y descarga la imagen; tardaba mas que el margen y el ALB la reemplazaba.
  # Solo hay cargo si el promedio de 24 h supera el 10 % de CPU; la API en reposo no llega (presupuesto con alertas).
  credit_specification {
    cpu_credits = "unlimited"
  }

  # IMDSv2 obligatorio; 2 saltos para que el SDK dentro del contenedor obtenga credenciales del rol.
  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
  }

  block_device_mappings {
    device_name = "/dev/xvda"

    ebs {
      volume_size           = 10
      volume_type           = "gp3"
      encrypted             = true
      delete_on_termination = true
    }
  }

  user_data = base64encode(templatefile("${path.module}/user-data.sh.tftpl", {
    region     = var.region
    registry   = split("/", data.aws_ecr_repository.api.repository_url)[0]
    image      = local.image
    ssm_prefix = var.ssm_prefix
    log_group  = aws_cloudwatch_log_group.api.name
  }))

  tag_specifications {
    resource_type = "instance"
    tags          = { Name = "musubi-api" }
  }
}

resource "aws_autoscaling_group" "api" {
  name                      = "musubi-api"
  min_size                  = var.instance_count
  max_size                  = var.instance_count
  desired_capacity          = var.instance_count
  vpc_zone_identifier       = aws_subnet.public[*].id
  target_group_arns         = [aws_lb_target_group.api.arn]
  health_check_type         = "ELB"
  health_check_grace_period = 600

  launch_template {
    id      = aws_launch_template.api.id
    version = aws_launch_template.api.latest_version
  }

  # Una version nueva de la imagen = plantilla nueva = reemplazo gradual (siempre queda 1 instancia sana).
  instance_refresh {
    strategy = "Rolling"

    preferences {
      min_healthy_percentage = 50
      instance_warmup        = 120
    }
  }

  tag {
    key                 = "Name"
    value               = "musubi-api"
    propagate_at_launch = true
  }
}
