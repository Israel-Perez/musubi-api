# Rol de las instancias: lo minimo para arrancar la API y que la API haga su trabajo.

data "aws_iam_policy_document" "ec2_trust" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "api" {
  name               = "musubi-api-instance"
  assume_role_policy = data.aws_iam_policy_document.ec2_trust.json
}

# Session Manager: entrar a una instancia sin SSH ni puertos abiertos.
resource "aws_iam_role_policy_attachment" "api_ssm_core" {
  role       = aws_iam_role.api.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_iam_policy_document" "api" {
  statement {
    sid       = "LeerConfiguracion"
    actions   = ["ssm:GetParametersByPath", "ssm:GetParameters", "ssm:GetParameter"]
    resources = ["arn:aws:ssm:${var.region}:${data.aws_caller_identity.current.account_id}:parameter${var.ssm_prefix}", "arn:aws:ssm:${var.region}:${data.aws_caller_identity.current.account_id}:parameter${var.ssm_prefix}/*"]
  }

  statement {
    sid       = "DescargarImagen"
    actions   = ["ecr:BatchGetImage", "ecr:GetDownloadUrlForLayer", "ecr:BatchCheckLayerAvailability"]
    resources = [data.aws_ecr_repository.api.arn]
  }

  statement {
    sid       = "AutenticarseEnEcr"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"]
  }

  statement {
    sid       = "EscribirLogs"
    actions   = ["logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams"]
    resources = ["${aws_cloudwatch_log_group.api.arn}:*"]
  }

  statement {
    sid       = "EnviarCorreo"
    actions   = ["ses:SendEmail"]
    resources = ["arn:aws:ses:${var.region}:${data.aws_caller_identity.current.account_id}:identity/*"]
  }

  statement {
    sid       = "Imagenes"
    actions   = ["s3:PutObject", "s3:GetObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.media.arn}/*"]
  }
}

resource "aws_iam_role_policy" "api" {
  name   = "musubi-api"
  role   = aws_iam_role.api.id
  policy = data.aws_iam_policy_document.api.json
}

resource "aws_iam_instance_profile" "api" {
  name = "musubi-api-instance"
  role = aws_iam_role.api.name
}
