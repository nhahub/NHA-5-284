# Issue 06 — Ansible: kubeadm Cluster + Tools Node Provisioning

| Field | Value |
|---|---|
| **Type** | 👤 Human-in-the-loop (live AWS nodes required) |
| **Blocked By** | #05 (nodes must be running; Ansible inventory from Terraform outputs) |
| **Tier** | Tier 1 (must) |

## Goal

Write Ansible playbooks that take freshly launched EC2 nodes and produce: a working kubeadm Kubernetes cluster (control plane + 2 workers), Calico CNI, and a configured tools node running Jenkins and SonarQube (with its own Postgres) under systemd. Everything is idempotent — re-running the playbook is safe.

> [!NOTE]
> **Time-box rule (from PRD §10.2):** If the cluster is not `Ready` by end of Day 4, switch to K3s. The Ansible role structure should isolate the kubeadm steps so the K3s fallback only requires swapping one role.

---

## Target Files / Modules

```
infra/
  ansible/
    inventory/
      aws_ec2.yml               # dynamic inventory plugin (aws_ec2) using Terraform tags
    group_vars/
      all.yml                   # kubernetes_version, pod_cidr, tools_node vars
    roles/
      common/                   # swap off, kernel modules, sysctl, containerd, runc
      kubeadm_control_plane/    # kubeadm init, kubeconfig, upload-certs
      kubeadm_worker/           # kubeadm join (token from control-plane role output)
      calico/                   # kubectl apply calico manifest (pinned version)
      k3s_server/               # (fallback) K3s single-node server
      k3s_agent/                # (fallback) K3s agent join
      tools_node/               # Jenkins + SonarQube + Postgres (docker compose or packages)
    site.yml                    # top-level playbook: common → control-plane → workers → calico → tools
    k3s_site.yml                # fallback playbook

Makefile                        # extend: make ansible-ping, make ansible-cluster,
                                #   make ansible-tools, make ansible-k3s (fallback)
```

---

## Tasks

1. **Dynamic inventory**: `aws_ec2.yml` using the `amazon.aws` collection; groups nodes by the `Role` tag set by Terraform.
2. **`common` role**: disable swap permanently; load `overlay` and `br_netfilter` kernel modules; set `net.bridge.bridge-nf-call-iptables=1`, `net.ipv4.ip_forward=1`; install containerd + runc from GitHub releases (pinned versions); write `/etc/containerd/config.toml` (SystemdCgroup = true).
3. **`kubeadm_control_plane` role**: install `kubeadm`, `kubelet`, `kubectl`; run `kubeadm init`; copy kubeconfig; output the join command as a registered variable.
4. **`kubeadm_worker` role**: install kubeadm/kubelet; run `kubeadm join` with the token from the control-plane role.
5. **`calico` role**: apply the Calico operator + CR manifest (pinned version); wait for all calico pods `Running`.
6. **`tools_node` role**: install Docker; run a `docker compose up -d` for `jenkins/jenkins:lts-jdk21` and `sonarqube:community` + their Postgres; persist data in named volumes; configure Jenkins initial admin password retrieval task.
7. **Idempotency**: all tasks use `creates:`, `when:`, or module-level idempotency guards so re-running is safe.
8. **K3s fallback roles**: minimal server + agent roles that produce an equivalent `~/.kube/config`.

---

## Definition of Done

- [ ] `make ansible-cluster` completes with zero failures; `kubectl get nodes` shows all 3 nodes `Ready`.
- [ ] `kubectl get pods -n kube-system` shows all system pods including calico `Running`.
- [ ] `make ansible-tools` completes; `curl http://<tools-node-ip>:8080` returns the Jenkins login page (via SSM port-forward).
- [ ] SonarQube UI is reachable on tools node port 9000 (via SSM port-forward).
- [ ] Re-running the playbook with no code changes exits with zero `changed` tasks.
- [ ] No SSH port 22 is opened; all Ansible connections use the SSM plugin (`ansible_connection: aws_ssm`).
- [ ] All component versions are pinned (no `latest`).
