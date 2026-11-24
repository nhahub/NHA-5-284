#!/bin/bash
set -euo pipefail

echo "=== 1. Updating system & installing Git + Fontconfig + Java 21 ==="
dnf update -y
dnf install -y git fontconfig java-21-amazon-corretto-headless

echo "=== 2. Adding Jenkins repository ==="
curl -fsSL -o /etc/yum.repos.d/jenkins.repo https://pkg.jenkins.io/rpm-stable/jenkins.repo
rpm --import https://pkg.jenkins.io/rpm-stable/jenkins.io-2026.key

echo "=== 3. Installing Jenkins ==="
dnf install -y jenkins

echo "=== 4. Tuning JVM Memory for t3.medium (Max 2GB) ==="
mkdir -p /etc/systemd/system/jenkins.service.d
cat <<EOF > /etc/systemd/system/jenkins.service.d/override.conf
[Service]
Environment="JAVA_OPTS=-Djava.awt.headless=true -Xms512m -Xmx2048m"
EOF

echo "=== 5. Starting and enabling Jenkins ==="
systemctl daemon-reload
systemctl enable --now jenkins

echo "=== 6. Waiting for Jenkins initial password generation ==="
for i in {1..30}; do
    if [ -f /var/lib/jenkins/secrets/initialAdminPassword ]; then
        echo "Jenkins initialized successfully!"
        break
    fi
    sleep 2
done

echo "=== 7. Storing K3s API credentials on Jenkins instance ==="
cat <<EOF > /etc/k3s-jenkins-info.env
K3S_URL="${controller_url}"
K3S_TOKEN="${k3s_token}"
EOF
chmod 600 /etc/k3s-jenkins-info.env
echo "${k3s_token}" > /var/lib/jenkins/k3s_token
chown -R jenkins:jenkins /var/lib/jenkins/k3s_token 2>/dev/null || true

echo "=== Setup Complete ==="