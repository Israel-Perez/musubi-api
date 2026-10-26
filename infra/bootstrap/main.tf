# Base de la cuenta (02-05): se aplica UNA vez desde la PC de un administrador (aws login).
# Lo que vive aqui sobrevive a infra-down: registro de imagenes, acceso de GitHub por OIDC y alertas de gasto.
# El bucket del estado lo crea scripts/bootstrap-state.ps1 (huevo y gallina: Terraform necesita el bucket antes).

terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.70"
    }
  }

  # Configuracion parcial: bucket/key/region llegan con -backend-config (infra/README.md).
  backend "s3" {}
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project   = "musubi"
      ManagedBy = "terraform"
      Stack     = "bootstrap"
    }
  }
}

data "aws_caller_identity" "current" {}

# ---------- Registro de imagenes de la API ----------

resource "aws_ecr_repository" "api" {
  name = "musubi-api"
  # Lineamiento 11: una version nunca se publica dos veces con contenido distinto.
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "AES256"
  }
}

resource "aws_ecr_lifecycle_policy" "api" {
  repository = aws_ecr_repository.api.name
  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Conserva las 15 imagenes mas recientes (costo de almacenamiento casi cero)"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 15
      }
      action = { type = "expire" }
    }]
  })
}

# ---------- GitHub Actions -> AWS sin llaves guardadas (OIDC) ----------

resource "aws_iam_openid_connect_provider" "github" {
  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]
}

data "aws_iam_policy_document" "github_trust" {
  statement {
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # Solo jobs del entorno "produccion" de musubi-api, que GitHub restringe a la rama main.
    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repository}:environment:${var.github_environment}"]
    }
  }
}

resource "aws_iam_role" "github_deploy" {
  name                 = "musubi-github-deploy"
  description          = "GitHub Actions (musubi-api, entorno ${var.github_environment}) crea, despliega y elimina la infraestructura"
  assume_role_policy   = data.aws_iam_policy_document.github_trust.json
  max_session_duration = 3600
}

# Terraform administra VPC, ALB, EC2, IAM, CloudFront y S3: el rol necesita permisos amplios.
# La proteccion esta en la confianza (un repo, un entorno, solo main), no en la politica (02-05, decision a revisar).
resource "aws_iam_role_policy_attachment" "github_deploy_admin" {
  role       = aws_iam_role.github_deploy.name
  policy_arn = "arn:aws:iam::aws:policy/AdministratorAccess"
}

# ---------- Alertas de gasto (plan gratuito: 100 USD de creditos) ----------

resource "aws_budgets_budget" "monthly" {
  name         = "musubi-mensual"
  budget_type  = "COST"
  limit_amount = "50"
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  # Sin creditos: si los contara, el gasto neto seria 0 y las alertas nunca sonarian.
  cost_types {
    include_credit = false
    include_refund = false
  }

  dynamic "notification" {
    for_each = [20, 50, 100]
    content {
      comparison_operator        = "GREATER_THAN"
      threshold                  = notification.value
      threshold_type             = "PERCENTAGE"
      notification_type          = "ACTUAL"
      subscriber_email_addresses = [var.alert_email]
    }
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.alert_email]
  }
}
