# Issue 05 — Terraform AWS Foundation: VPC, Nodes, NLB & IAM

| Field | Value |
|---|---|
| **Type** | 👤 Human-in-the-loop (AWS account credentials required) |
| **Blocked By** | #01 (Makefile pattern established); Day-0 checklist from PRD §10.3 must be complete (MFA, admin user, budget alert, Service Quota check, domain in Cloudflare) |
| **Tier** | Tier 1 (must) |

## Goal

Write all Terraform code that provisions the AWS layer from scratch: VPC, subnets, security groups, EC2 nodes (control plane × 1, workers × 2, tools node × 1), Network Load Balancer, IAM instance role, and the S3 remote-state bucket bootstrap. `terraform apply` must succeed; `terraform destroy` must leave nothing running except the state bucket.

> [!CAUTION]
> This creates **real AWS resources that cost money**. Run `make down` after every session. Verify the AWS Budgets alert is active before the first `apply`.

> [!IMPORTANT]
> No NAT gateways. All nodes use public subnets with per-node security groups. IMDSv2 required, hop limit 1. Port 22 closed — access via SSM Session Manager only.

---

## Target Files / Modules

```
infra/
  terraform/
    bootstrap/
      main.tf                   # S3 state bucket (versioning, encryption, public-access block)
                                # Run once; excluded from make down
    main.tf                     # Provider, remote backend config
    variables.tf                # instance_types, region, cluster_name, node_counts
    outputs.tf                  # node IPs, NLB DNS name, IAM role ARN
    vpc.tf                      # VPC, 2 public subnets, IGW, route table
    security_groups.tf          # control-plane SG, worker SG, tools SG, NLB SG
    nodes.tf                    # aws_instance for each role (using variables)
    nlb.tf                      # NLB (TCP 80 + 443) → worker NodePort target group
    iam.tf                      # instance profile: SSM + minimal custom policy; IMDSv2

Makefile                        # extend: make tf-init, make tf-plan, make tf-apply,
                                #   make tf-destroy (= make down for infra layer)
```

---

## Tasks

1. **Bootstrap** (`infra/terraform/bootstrap/`): S3 bucket with versioning, AES-256 encryption, block-public-access, bucket-key enabled. Must be run manually once; must **not** be part of `make down`.
2. **VPC**: single VPC, 2 public subnets in different AZs, Internet Gateway, one route table. No NAT gateway.
3. **Security groups**:
   - `control-plane-sg`: allow inbound 6443 from worker + tools SGs; all from within VPC.
   - `worker-sg`: allow inbound 30000-32767 from NLB SG (NodePort); 10250 from control-plane-sg.
   - `tools-sg`: allow inbound nothing public; outbound all.
   - `nlb-sg`: inbound 80, 443 from `0.0.0.0/0`; outbound to worker-sg NodePorts.
4. **EC2 nodes** (Terraform `for_each` over a `roles` map): control-plane (c7i-flex.large), worker ×2 (m7i-flex.large), tools (m7i-flex.large). All: IMDSv2 (`http_tokens = required`, `http_put_response_hop_limit = 1`), instance profile, encrypted root volume (gp3, 30 GB), no key pair.
5. **NLB**: TCP listeners 80 → worker NodePort 30080, 443 → worker NodePort 30443. Health check on NodePort 30080.
6. **IAM**: custom policy for SSM Session Manager only (`ssm:*`, `ssmmessages:*`, `ec2messages:*`). No S3, no ECR, no broad permissions.
7. **Outputs**: node private IPs (for Ansible inventory), NLB DNS, instance profile ARN.
8. **Makefile**: `make tf-plan` runs `terraform plan -out=tfplan`; `make tf-apply` applies; `make tf-destroy` destroys (with a manual confirmation prompt via `read`).

---

## Definition of Done

- [ ] `make tf-apply` succeeds from a clean state; all 4 nodes are `running` in EC2 console.
- [ ] SSM Session Manager connects to each node (`aws ssm start-session --target <id>`); no port 22 needed.
- [ ] NLB DNS resolves and responds on TCP 80 and 443 (even if the backend is not yet configured).
- [ ] `terraform plan` after a successful apply shows **zero** changes.
- [ ] `make tf-destroy` removes all resources; EC2 console shows no running instances.
- [ ] No secrets, AWS credentials or account IDs are committed to Git.
- [ ] `terraform state` contains no application-layer secrets.
- [ ] Checkov scan (`checkov -d infra/terraform`) passes on the critical rules (or suppressions are commented and justified).
