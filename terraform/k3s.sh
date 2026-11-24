#!/bin/bash
set -euo pipefail

export K3S_TOKEN='${k3s_token}'

# 1. Create static API token file for Jenkins with cluster-admin privileges
mkdir -p /etc/rancher/k3s
cat <<EOF > /etc/rancher/k3s/token.csv
${k3s_token},jenkins,jenkins,"system:masters"
EOF
chmod 600 /etc/rancher/k3s/token.csv

# 2. Install K3s server with static token auth enabled
curl -sfL https://get.k3s.io | INSTALL_K3S_EXEC="server --write-kubeconfig-mode 644 --kube-apiserver-arg=token-auth-file=/etc/rancher/k3s/token.csv" sh -

# 3. Wait for K3s API to become ready
until kubectl get nodes 2>/dev/null; do
    sleep 3
done

# 4. Create jenkins namespace for agent pods
kubectl create namespace jenkins --dry-run=client -o yaml | kubectl apply -f -