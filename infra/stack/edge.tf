# CloudFront: HTTPS con dominio propio (api.musubiapp.com.mx) delante del balanceador,
# y CDN de imagenes (lineamiento 15) desde un bucket privado.

# Politicas administradas por AWS (IDs publicos y fijos).
locals {
  cache_policy_disabled     = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad" # Managed-CachingDisabled
  cache_policy_optimized    = "658327ea-f89d-4fab-a63d-7e88639e58f6" # Managed-CachingOptimized
  response_security_headers = "67f7725c-6f97-4210-82d7-5512b31e9d03" # Managed-SecurityHeadersPolicy (HSTS, nosniff...)
}

# La API recibe todo lo del cliente mas la IP real y el esquema que vio CloudFront.
resource "aws_cloudfront_origin_request_policy" "api" {
  name    = "musubi-api"
  comment = "Cabeceras del cliente + CloudFront-Viewer-Address y CloudFront-Forwarded-Proto"

  headers_config {
    header_behavior = "allViewerAndWhitelistCloudFront"

    headers {
      items = ["CloudFront-Viewer-Address", "CloudFront-Forwarded-Proto"]
    }
  }

  cookies_config {
    cookie_behavior = "none"
  }

  query_strings_config {
    query_string_behavior = "all"
  }
}

resource "aws_s3_bucket" "media" {
  bucket_prefix = "musubi-media-"
  force_destroy = true
}

resource "aws_s3_bucket_public_access_block" "media" {
  bucket                  = aws_s3_bucket.media.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "media" {
  bucket = aws_s3_bucket.media.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_cloudfront_origin_access_control" "media" {
  name                              = "musubi-media"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# El certificado se pide fuera de Terraform (infra/README.md, "Dominio"): su validacion es un
# CNAME en Cloudflare y debe estar emitido antes del apply. Renovacion automatica de ACM.
data "aws_acm_certificate" "api" {
  count       = var.api_domain == "" ? 0 : 1
  domain      = var.api_domain
  statuses    = ["ISSUED"]
  most_recent = true
}

# Con dominio, TLS 1.2 como minimo; sin dominio (api_domain vacio) CloudFront no permite fijarlo.
resource "aws_cloudfront_distribution" "main" { # nosemgrep: terraform.aws.security.aws-cloudfront-insecure-tls.aws-insecure-cloudfront-distribution-tls-version
  enabled         = true
  comment         = "musubi-api + imagenes"
  price_class     = "PriceClass_100"
  http_version    = "http2and3"
  is_ipv6_enabled = true
  aliases         = var.api_domain == "" ? [] : [var.api_domain]

  origin {
    origin_id   = "api"
    domain_name = aws_lb.api.dns_name

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only" # El ALB solo acepta a CloudFront (X-Origin-Verify). NOTE (02-06): HTTPS hasta el ALB.
      origin_ssl_protocols   = ["TLSv1.2"]
    }

    custom_header {
      name  = "X-Origin-Verify"
      value = random_password.origin_secret.result
    }
  }

  origin {
    origin_id                = "media"
    domain_name              = aws_s3_bucket.media.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.media.id
  }

  # API: sin cache; HTTPS obligatorio (un POST no sobrevive a una redireccion).
  default_cache_behavior {
    target_origin_id           = "api"
    viewer_protocol_policy     = "https-only"
    allowed_methods            = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods             = ["GET", "HEAD"]
    cache_policy_id            = local.cache_policy_disabled
    origin_request_policy_id   = aws_cloudfront_origin_request_policy.api.id
    response_headers_policy_id = local.response_security_headers
    compress                   = true
  }

  # Imagenes (HU-06): CDN con cache.
  ordered_cache_behavior {
    path_pattern               = "/media/*"
    target_origin_id           = "media"
    viewer_protocol_policy     = "https-only"
    allowed_methods            = ["GET", "HEAD"]
    cached_methods             = ["GET", "HEAD"]
    cache_policy_id            = local.cache_policy_optimized
    response_headers_policy_id = local.response_security_headers
    compress                   = true
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = var.api_domain == ""
    acm_certificate_arn            = var.api_domain == "" ? null : data.aws_acm_certificate.api[0].arn
    ssl_support_method             = var.api_domain == "" ? null : "sni-only"
    minimum_protocol_version       = var.api_domain == "" ? "TLSv1" : "TLSv1.2_2021"
  }
}

data "aws_iam_policy_document" "media" {
  statement {
    sid       = "SoloCloudFront"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.media.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.main.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "media" {
  bucket     = aws_s3_bucket.media.id
  policy     = data.aws_iam_policy_document.media.json
  depends_on = [aws_s3_bucket_public_access_block.media]
}
