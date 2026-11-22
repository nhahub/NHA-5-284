terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }
}

resource "aws_instance" "this" {
  ami                    = var.ami_id
  instance_type          = var.instance_type
  subnet_id              = var.subnet_id
  vpc_security_group_ids = var.security_group_ids
  key_name               = var.key_name
  user_data              = var.user_data

  root_block_device {
    volume_size           = var.root_volume_size
    volume_type           = "gp2"
    encrypted             = false
    delete_on_termination = true
  }

  tags = merge(
    var.tags,
    {
      Name = var.instance_name
    }
  )
}
