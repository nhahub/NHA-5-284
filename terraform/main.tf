terraform {
  required_version = ">= 1.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

# Data source for latest Amazon Linux 2023 AMI
data "aws_ami" "amazon_linux" {
  most_recent = true
  owners      = ["amazon"]

  filter {
    name   = "name"
    values = ["al2023-ami-*-x86_64"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

# VPC
resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name = "${var.project_name}-vpc"
  }
}

# Subnet
resource "aws_subnet" "main" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = var.subnet_cidr
  availability_zone       = var.availability_zone
  map_public_ip_on_launch = true

  tags = {
    Name = "${var.project_name}-subnet"
  }
}

# Internet Gateway
resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name = "${var.project_name}-igw"
  }
}

# Route Table
resource "aws_route_table" "main" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = {
    Name = "${var.project_name}-rt"
  }
}

# Route Table Association
resource "aws_route_table_association" "main" {
  subnet_id      = aws_subnet.main.id
  route_table_id = aws_route_table.main.id
}

# Security Group
resource "aws_security_group" "ec2" {
  name        = "${var.project_name}-sg"
  description = "Security group for EC2 instance"
  vpc_id      = aws_vpc.main.id

  ingress {
    description = "SSH"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = [var.allowed_ssh_cidr]
  }
        # Allow all internal communication between cluster nodes
  ingress {
        description = "Allow cluster nodes internal traffic"
        from_port   = 0
        to_port     = 0
        protocol    = "-1"
        self        = true
  }
    

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.project_name}-sg"
  }
}


resource "random_password" "k3s_token" {
  length  = 48
  special = false
}
# EC2 Instance Module
module "controller" {
  source = "./modules/ec2"

  instance_name      = "${var.project_name}-controller-instance"
  ami_id             = data.aws_ami.amazon_linux.id
  instance_type      = var.k3s_controller_instance_type
  subnet_id          = aws_subnet.main.id
  security_group_ids = [aws_security_group.ec2.id]
  key_name           = var.key_name
  user_data          = templatefile("${path.module}/${var.controller_script}",{
    k3s_token = random_password.k3s_token.result
  })

  tags = {
    Job=      "Controller"
  }
}



module "worker" {
  source = "./modules/ec2"

  instance_name      = "${var.project_name}-worker-instance"
  ami_id             = data.aws_ami.amazon_linux.id
  instance_type      = var.k3s_worker_instance_type
  subnet_id          = aws_subnet.main.id
  security_group_ids = [aws_security_group.ec2.id]
  key_name           = var.key_name

  user_data = templatefile("${path.module}/${var.worker_script}", {
  k3s_token      = random_password.k3s_token.result
  controller_url = "https://${module.controller.private_ip}:6443"})
depends_on = [ module.controller ]
  tags = {
    Job=      "Worker"
    
  }
}