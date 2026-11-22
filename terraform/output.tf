output "vpc_id" {
  description = "ID of the VPC"
  value       = aws_vpc.main.id
}

output "subnet_id" {
  description = "ID of the subnet"
  value       = aws_subnet.main.id
}

output "security_group_id" {
  description = "ID of the security group"
  value       = aws_security_group.ec2.id
}

output "worker_instance_id" {
  description = "ID of the EC2 instance"
  value       = module.worker.instance_id
}

output "worker_public_ip" {
  description = "Public IP of the worker EC2 instance"
  value       = module.worker.public_ip
}

output "worker_private_ip" {
  description = "Private IP of the worker EC2 instance"
  value       = module.worker.private_ip
}


output "controller_instance_id" {
  description = "ID of the controller EC2 instance"
  value       = module.controller.instance_id
}

output "controller_public_ip" {
  description = "Public IP of the worker controller EC2 instance"
  value       = module.controller.public_ip
}

output "controller_private_ip" {
  description = "Private IP of the controller EC2 instance"
  value       = module.controller.private_ip
}