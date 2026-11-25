
# Security Group
resource "aws_security_group" "k3s" {
  name        = "${var.project_name}-k3s-sg"
  description = "Security group for EC2 instance"
  vpc_id      = aws_vpc.main.id

  ingress {
    description = "SSH"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = [var.allowed_ssh_cidr]
  }

  ingress {
    description = "HTTP / K3s Ingress / LoadBalancer"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = [var.allowed_ssh_cidr]
  }

  ingress {
    description = "Jenkins Web UI / HTTP Alt"
    from_port   = 8080
    to_port     = 8080
    protocol    = "tcp"
    cidr_blocks = [var.allowed_ssh_cidr]
  }

  ingress {
    description = "Kubernetes NodePort Services"
    from_port   = 30000
    to_port     = 32767
    protocol    = "tcp"
    cidr_blocks = [var.allowed_ssh_cidr]
  }

  # Allow all internal communication between cluster nodes in the subnet
  ingress {
    description = "Allow internal subnet traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = [var.subnet_cidr]
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
  security_group_ids = [aws_security_group.k3s.id]
  key_name           = var.key_name
  user_data = templatefile("${path.module}/${var.controller_script}", {
    k3s_token = random_password.k3s_token.result
  })

  tags = {
    Job = "Controller"
  }
}



module "worker" {
  source = "./modules/ec2"

  instance_name      = "${var.project_name}-worker-instance"
  ami_id             = data.aws_ami.amazon_linux.id
  instance_type      = var.k3s_worker_instance_type
  subnet_id          = aws_subnet.main.id
  security_group_ids = [aws_security_group.k3s.id]
  key_name           = var.key_name

  user_data = templatefile("${path.module}/${var.worker_script}", {
    k3s_token = random_password.k3s_token.result
  controller_url = "https://${module.controller.private_ip}:6443" })
  depends_on = [module.controller]
  tags = {
    Job = "Worker"

  }
}