aws_region        = "us-east-1"
project_name      = "Tamer"
vpc_cidr          = "10.0.0.0/16"
subnet_cidr       = "10.0.1.0/24"
availability_zone = "us-east-1a"

k3s_controller_instance_type = "t3.small"
k3s_worker_instance_type     = "t3.small"


key_name = "vockey"
# key_name        = "your-key-pair-name"  # Optional: uncomment if you have a key pair
allowed_ssh_cidr  = "0.0.0.0/0" # Change to your IP for better security
controller_script = "k3s.sh"
worker_script     = "k3s_worker.sh"

jenkins_instance_type = "t3.medium"
jenkins_script        = "jenkins.sh"