terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.70"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
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
      Stack     = "stack"
      Version   = trimspace(file("${path.module}/../VERSION"))
    }
  }
}
