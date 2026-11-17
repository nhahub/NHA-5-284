# PRD: Secure DevSecOps Delivery Platform for a Demo Banking App

**Owner:** Mohamed · **Project:** DevOps course graduation project · **Status:** Draft v1, 15 Nov 2026 · **Deadline:** about 3 weeks from this date

## 1. Overview

### 1.1 Purpose

Build a secure, fully automated delivery platform on the owner's own AWS Free-plan account and use it to ship a basic demo banking application. The graded outcome is the **DevSecOps cycle**: a code change travels from pull request to a live, observable, HTTPS deployment, with security gates on the way and a clean rollback. The banking app exists only to give the pipeline something meaningful to protect.

### 1.2 Success criteria

1. A live, scripted demo (10 to 15 minutes) shows: a pull request with a planted problem is blocked by a security gate, the fix passes, the merge deploys automatically, the result is visible in monitoring, and a rollback is done with a single `git revert`.
2. The whole platform is rebuilt from code with `make up` and removed with `make down`, with a recorded rebuild time.
3. The public surface is only the banking app over HTTPS on a real domain; all admin tools are private.
4. No plaintext secret exists in any Git repository or Terraform state.
5. A recorded backup of one full successful demo run exists.

### 1.3 Context and constraints

- Target environment is the owner's own AWS **Free plan** account. Credits are a countdown (up to about $200 over 6 months, the account closes when they run out), so every session is short and ends with `make down`.
- Free-plan EC2 limits apply: eligible types are t3.micro, t3.small, t4g.micro, t4g.small, c7i-flex.large and m7i-flex.large. The design uses only these.
- Solo delivery on a 3-week deadline. A teammate builds the frontend code; the owner deploys and secures it. Team-splitting is out of scope for this document.
- The AWS Academy sandbox is **not** a target.

## 2. Scope

### 2.1 In scope

- Terraform and Ansible provisioning of a self-managed kubeadm Kubernetes cluster, a tools node, a network load balancer and supporting AWS resources.
- A basic banking API (ASP.NET Core and PostgreSQL) and a separate frontend, each containerised and deployed with Helm through ArgoCD.
- A Jenkins CI pipeline with SonarQube and security scanning, image builds, GHCR publishing and automatic GitOps updates.
- HTTPS on a purchased domain with Cloudflare DNS and Let's Encrypt.
- Monitoring, alerting, backups and an operations runbook.

### 2.2 Non-goals

- No staging or per-PR preview environments (one production environment).
- No automatic rollback or canary releases (rollback is a manual `git revert`).
- No service mesh, mTLS between pods, multi-cluster or multi-region setup; no highly available control plane.
- No real money, real payment gateways or real customer data (seeded demo accounts only).
- No EKS, ECR, RDS, Secrets Manager or Vault.
- No public admin tools; no GitHub webhooks at first (polling instead).
- No frontend development by the owner.
- No autoscaling of nodes or pods.
- End-to-end tests only if time allows.

Each non-goal gets a one-sentence "what I would add in production" entry in the report.

## 3. Users and flows

| Role | Who | What they do |
| --- | --- | --- |
| Platform owner | Mohamed | Builds and operates the platform, writes pipeline and infrastructure code, runs the demo |
| Frontend developer | Teammate | Builds the frontend image's code and pushes to the frontend repo |
| Examiner | Course grader | Watches the live demo, reads the report and repos |
| Demo customer | Seeded user | Registers, logs in, views accounts, transfers money in the demo |

### 3.1 Developer flow (steady state)

1. Developer opens a pull request against `main` in an app repo.
2. Jenkins runs the required checks (tests, gitleaks, SonarQube, Trivy). The PR cannot merge until they pass.
3. After merge, Jenkins builds the image, scans it, pushes it to private GHCR tagged with the commit SHA, and a bot commits the new tag to `gitops-repo` (tag path only).
4. ArgoCD detects the change. A PreSync job runs database migrations (API only), then a rolling update starts.
5. New pods receive traffic only after readiness probes pass. Grafana and logs show the result.
6. Rollback: `git revert` of the GitOps commit; ArgoCD restores the previous version.

### 3.2 Session lifecycle

`make up` (Terraform, then Ansible, then GitOps bootstrap) builds everything; `make down` destroys everything except the Terraform state bucket. A longer-lived copy is kept only for demo day. Anything worth keeping lives outside AWS: GHCR images, Git repos, the sealing key (password manager) and database backups.

### 3.3 Live demo script

1. Open a PR containing a planted problem (a hardcoded secret or a vulnerable dependency). The gate fails and the merge is blocked.
2. Fix it; checks go green.
3. Merge. Show Jenkins building, scanning and pushing, then the bot's GitOps commit.
4. Show ArgoCD syncing: the migration job, then the rolling update.
5. Use the app on the domain (register, login, transfer).
6. Show Grafana metrics and logs for that request.
7. Roll back with `git revert`; show ArgoCD restoring the previous version.
8. The rebuild is run before the session; its timing comes from a recording.

During the demo, a manual "Scan Repository Now" click in Jenkins replaces the polling wait.

## 4. The application

### 4.1 Backend (API)

One ASP.NET Core service (modular monolith) with PostgreSQL via Entity Framework Core. Money uses a decimal type; a transfer is a single database transaction.

**Data model:** Users (id, email, password hash, role), Accounts (id, owner, account number, balance, currency), Transactions (id, from account, to account, amount, status, created time).

**Endpoints (REST, JWT):** `POST /auth/register`, `POST /auth/login`, `GET /accounts`, `POST /accounts`, `POST /transfers`, `GET /accounts/{id}/transactions`, plus `/health/live`, `/health/ready` and `/metrics` for probes and Prometheus.

**Rules:** users can see only their own accounts; the running API connects with a least-privilege database user; Swagger is off by default and enabled only through a configuration flag; idempotency keys and an audit-log table are later additions, not part of the first slice.

### 4.2 Frontend

A separate app with its own Dockerfile, built by the teammate. It calls the API with relative URLs (`/api/...`) so there is one origin and no CORS configuration. Low priority; for the owner it counts only as a workload deployed and secured in the cluster.

### 4.3 Public routing

One domain, one certificate, one DNS record. Traefik routes `/` to the frontend and `/api` to the API. Health, metrics and Swagger are not routed publicly.

## 5. Architecture

### 5.1 AWS and nodes

| Node | Type | Purpose |
| --- | --- | --- |
| Control plane (1) | c7i-flex.large (4 GB) | kubeadm control plane |
| Workers (2) | m7i-flex.large (8 GB) | Apps, Postgres, monitoring, Traefik, build pods |
| Tools node (1) | m7i-flex.large (8 GB) | Jenkins controller, SonarQube and its Postgres (Ansible-managed, outside the cluster) |

Instance types and counts are Terraform variables. Needs 8 vCPUs, so check the account's Service Quotas early. Rough cost: $0.35 to $0.45 per hour including the load balancer, public IPs and disks (verify with the pricing calculator).

### 5.2 Kubernetes and networking

- **Install:** kubeadm via Ansible with Calico. Fallback to K3s if the cluster is not running by the end of day 4.
- **Entry point:** a Network Load Balancer (TCP 80 and 443) created by Terraform, forwarding to Traefik NodePorts. TLS terminates in the cluster.
- **Ingress:** Traefik (ingress-nginx was archived in March 2026), standard Ingress resources, pinned chart, non-root, dashboard off.
- **DNS and TLS:** Cloudflare DNS (DNS-only mode so encrypted traffic reaches the cluster), updated by script via a zone-scoped API token on every `make up`; cert-manager with Let's Encrypt (HTTP-01).
- **Database:** PostgreSQL as an in-cluster StatefulSet on local storage, a seed job on every rebuild and a scheduled backup copied off the servers.
- **Namespaces:** app, monitoring, argocd, cert-manager, traefik and a separate `jenkins-agents` namespace for build pods.

### 5.3 Repositories

- `api-repo`: API code, Dockerfile, Jenkinsfile, tests.
- `web-repo`: frontend code, Dockerfile, Jenkinsfile, tests.
- `gitops-repo`: one Helm chart per app (`charts/api`, `charts/web`), one ArgoCD Application each.
- Infrastructure code (Terraform, Ansible, Makefile, runbook) in its own repo with checkov and Trivy config scanning.

### 5.4 Terraform state

Remote state in an S3 bucket (versioning, encryption, public access blocked, lock-file locking), created once by a bootstrap step and excluded from `make down`. State must hold no application secrets.

## 6. CI/CD pipeline

### 6.1 Triggers

Jenkins polls each repo about once a minute (multibranch pipeline). Jenkins is not exposed to the internet; it reports statuses to GitHub outbound. Webhooks are a later upgrade.

### 6.2 Stages and gates

| Stage | Tool | On a finding |
| --- | --- | --- |
| Build and unit tests | dotnet test | Block |
| Integration tests | Real PostgreSQL as a second container in the build pod | Block |
| Secrets in code | gitleaks | Block (any finding) |
| Static analysis and quality | SonarQube quality gate (modest start, about 60% coverage on new code) | Block; on the PR if the installed edition supports PR analysis, otherwise on merge before the image push |
| Extra static rules | Semgrep | Warn only |
| Dependency vulnerabilities | Trivy | Block on HIGH/CRITICAL with a fix |
| Image build | Rootless BuildKit in a build pod (Kaniko fallback) | Block on failure |
| Image scan | Trivy on the built image | Block on HIGH/CRITICAL with a fix |
| Dockerfile, Helm and Terraform checks | Trivy config / checkov | Warn, block a short list of critical rules |
| Publish | Push to private GHCR tagged with commit SHA | Block on failure |
| GitOps update | Bot commits new tag to `gitops-repo` (tag path only) | Block on failure |
| Running-app scan (optional) | OWASP ZAP baseline | Report only |

### 6.3 Build pods

Build pods run in the `jenkins-agents` namespace, which relaxes pod-security only as far as rootless BuildKit needs, with strict NetworkPolicies and no secrets except the GHCR push token. If BuildKit is not working after one day of effort, switch to Kaniko (pinned by digest or self-built from source).

### 6.4 Deployment and failure handling

- ArgoCD auto-sync with self-heal on; a failed sync raises an alert.
- **Migrations:** an EF Core migration bundle runs as an ArgoCD PreSync Job using a separate owner-level database credential. The running API uses a least-privilege user. Migrations must stay backward-compatible, because a rollback reverts the API but not applied migrations.
- **Rollout safety:** readiness and liveness probes (`/health/ready`, `/health/live`), rolling update with `maxUnavailable: 0`. A broken version never receives traffic and the old pods keep serving.
- **Rollback:** `git revert` of the GitOps commit. No Argo Rollouts or canary releases.

## 7. Security requirements

| Area | Requirement |
| --- | --- |
| Secrets | Sealed Secrets: encrypted values in `gitops-repo`, decrypted in the cluster. The sealing key is exported after first install, stored in a password manager (never Git) and restored by the bootstrap before the controller starts. Tested once with a full rebuild. |
| AWS identity | Root account with MFA and not used for work; a separate admin user or role with MFA runs Terraform. |
| Instance permissions | Minimal custom IAM role created by Terraform with SSM Session Manager access only. IMDSv2 required, hop limit 1. Port 22 closed. |
| Exposure | Only the banking app (80/443 on the NLB) is public. Jenkins, ArgoCD, Grafana and SonarQube are reached through SSM or `kubectl port-forward`. |
| App edge | HTTPS only, rate limiting on login, security headers, Swagger off by default. |
| Network | Default-deny NetworkPolicies per namespace with explicit allows; Traefik reachable only from the NLB and monitoring. |
| Pods | Non-root, read-only filesystems where possible, resource limits, pinned image and chart versions. |
| Git | Private repos preferred; PRs required; required status checks; no force-push or deletion of `main`; secret scanning and push protection; GitOps bot limited to the tag path; no required approvals until a teammate joins. |
| Registry | Private GHCR images; the cluster pulls with a read-only token stored as a SealedSecret. |
| Cluster hardening | Etcd secret encryption, restricted API port and kubelet settings; kube-bench pass is a Tier 3 polish item. |

## 8. Observability

- Prometheus and Grafana (kube-prometheus-stack), explicit memory limits and about 2 days of retention.
- **Dashboards (3):** cluster health, the API (request rate, errors, latency from `/metrics`) and the database.
- **Alerts (4):** pod crash-looping, node not ready, ArgoCD sync failing, Jenkins build failing. Visible in Grafana and Alertmanager first; email or Slack delivery decided later.
- Loki for logs (failed logins and failed transfers). Loki is the first component to drop if memory is tight.

## 9. Operations and cost

- **Makefile targets:** `make up`, `make down`, plus a bootstrap target for the state bucket. `make down` ends with a check that lists anything still running.
- **Budgets:** an AWS Budgets alert is set before the first build; no NAT gateways.
- **Rebuild rehearsals:** at least two timed full rebuilds before the deadline; the timing is reported.
- **Runbook:** how to build, operate, back up, restore, roll back and tear down the platform.

## 10. Priorities and schedule

### 10.1 Tiers

- **Tier 1 (must):** Terraform and Ansible build the kubeadm cluster, tools node and NLB; API and Postgres via Helm and ArgoCD; Jenkins pipeline with tests, gitleaks, Trivy, SonarQube gate, build, GHCR push and GitOps tag update; HTTPS on the domain; Sealed Secrets.
- **Tier 2 (important):** Prometheus, Grafana and the four alerts; migration PreSync job; NetworkPolicies and pod hardening; the rollback demo; frontend deployed through Helm; S3 remote state.
- **Tier 3 (cut first):** Loki, Semgrep, ZAP, extra hardening and kube-bench polish, end-to-end tests, webhooks.

### 10.2 Time-box rules

- No running kubeadm cluster by the end of day 4: switch to K3s.
- Rootless BuildKit not building images after one day: switch to Kaniko.

### 10.3 Three-week plan

- **Day 0 (lead-time items):** finish AWS activation; MFA on root; admin user; budget alert; buy the domain and move DNS to Cloudflare; check Service Quotas (8 vCPUs); check GitHub plan limits and the GHCR token type; create the repos; bootstrap the state bucket if quick.
- **Week 1:** Milestone 0 probe; Terraform and Ansible for the cluster and tools node; Traefik, cert-manager and HTTPS on the domain; Sealed Secrets; ArgoCD with a minimal API skeleton; Jenkins and SonarQube on the tools node.
- **Week 2:** the full pipeline end to end; minimal API code and tests; Postgres StatefulSet and the migration job; frontend deployment when the teammate's image is ready. Tier 1 done by mid-week.
- **Week 3:** Tier 2 (monitoring and alerts, NetworkPolicies, hardening, rollback demo); runbook, diagram and report; timed rebuild rehearsals; recorded demo. Feature freeze three days before the deadline, with those days used for rehearsal and the report.

## 11. Milestone 0 checks (open items)

Quick probes whose results choose between options already defined:

1. Does the installed SonarQube edition analyze pull requests? (decides PR gate vs merge gate)
2. Which GHCR token type works for pushing and pulling?
3. Is the vCPU quota enough for 8 vCPUs?
4. Do the Free-plan instance types launch correctly (`describe-instance-types` with the free-tier filter)?
5. Does rootless BuildKit run under the chosen pod-security settings?
6. Does the GitHub plan support branch protection on private repos (or does the Student Developer Pack cover it)?
7. Where do alerts go (email or Slack)?
8. Who writes the API code? This plan assumes the owner, with a deliberately minimal scope.

## 12. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| 3 weeks is tight for the scope | Tiering and cut line; time-box rules; feature freeze |
| Credits run out or the account closes | Short sessions, `make down`, budget alert, recorded demo as backup |
| Live demo fails (AWS, DNS, certificates) | Pre-built environment, scripted run, recorded backup, manual scan trigger |
| kubeadm or BuildKit consumes days | Time-boxed fallbacks (K3s, Kaniko) |
| 4 GB / 8 GB nodes run out of memory | Memory limits, short retention, Loki dropped first |
| Sealing key lost, so secrets cannot be decrypted | Key backed up in a password manager and restore tested once |
| Orphaned resources keep billing | Remote state, `make down` checks, budget alert |
| Rollback does not undo a migration | Backward-compatible migrations rule |
| Unpatched or unmaintained components | Traefik instead of ingress-nginx; pinned versions; Trivy gates |

## 13. Acceptance criteria (definition of done)

- The demo script in section 3.3 runs end to end on the live domain, including the blocked PR and the `git revert` rollback.
- `make up` produces a working platform from an empty AWS account state, and `make down` leaves no running resources except the state bucket.
- Both app pipelines enforce the gates in section 6.2 and show their results on pull requests.
- No secret appears in any repo, in Terraform state or in the public routes; gitleaks is clean.
- Only 80/443 on the NLB are reachable from the internet.
- Three dashboards and four alerts exist and are shown in the demo.
- A runbook, an architecture diagram, repo READMEs, a written report and a recorded backup demo are delivered.

## 14. Future work (for the report)

Staging and preview environments, canary and automatic rollback (Argo Rollouts), Gateway API, service mesh and mTLS, highly available control plane and multi-region, RDS or managed databases, ECR with IAM roles instead of long-lived tokens, Vault or an external secrets manager, GitHub webhooks, end-to-end tests with ZAP in the pipeline, node and pod autoscaling, idempotency keys and a full audit log in the application.
