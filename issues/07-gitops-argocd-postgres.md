# Issue 07 — GitOps: ArgoCD, Helm Charts, Postgres StatefulSet & Migration Job

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent (chart authoring) + 👤 Human-in-the-loop (first ArgoCD sync to live cluster) |
| **Blocked By** | #06 (working cluster); #04 (API code complete so migration bundle can be built) |
| **Tier** | Tier 1 (must) |

## Goal

Create the `gitops-repo` structure: Helm charts for the API and (stub) frontend, ArgoCD Application manifests, the PostgreSQL StatefulSet, the EF Core migration PreSync Job, and Sealed Secrets placeholders. After this issue, `argocd app sync banking-api` deploys the API and runs migrations end-to-end on the live cluster.

> [!IMPORTANT]
> The migration job uses a **separate owner-level DB credential** (SealedSecret). The running API uses a **least-privilege** DB user (SealedSecret). These are two different secrets — do not share them.

---

## Target Files / Modules

```
gitops-repo/                    # separate Git repository
  charts/
    api/
      Chart.yaml                # name: banking-api, version: 0.1.0
      values.yaml               # image.repository, image.tag, replicas, resources,
                                #   probes, env refs to secrets
      templates/
        deployment.yaml         # non-root (runAsNonRoot: true), readOnlyRootFilesystem,
                                #   resource limits, liveness + readiness probes
        service.yaml            # ClusterIP
        ingress.yaml            # Traefik Ingress, host from values
        migration-job.yaml      # ArgoCD PreSync Job; runs migration bundle image;
                                #   uses DB_OWNER SealedSecret
        sealed-secret-db.yaml   # SealedSecret: DB_OWNER conn string (placeholder)
        sealed-secret-api.yaml  # SealedSecret: DB_APP conn string + JWT key (placeholder)
        hpa.yaml                # (stub, minReplicas=1 — no autoscaling per PRD)

    web/
      Chart.yaml                # stub — mirrors api chart structure
      values.yaml
      templates/
        deployment.yaml
        service.yaml
        ingress.yaml

  postgres/
    Chart.yaml                  # or plain manifests; wraps StatefulSet + Services
    templates/
      statefulset.yaml          # postgres:16, PVC local-storage, resource limits
      service.yaml              # ClusterIP (postgres-svc.app.svc.cluster.local)
      seed-job.yaml             # one-time Job: psql inserts demo accounts
      backup-cronjob.yaml       # CronJob: pg_dump → S3 (aws cli) or local copy

  argocd/
    banking-api-app.yaml        # ArgoCD Application: auto-sync, self-heal, prune
    banking-web-app.yaml        # ArgoCD Application stub
    postgres-app.yaml           # ArgoCD Application for postgres chart

  Makefile / scripts/
    seal.sh                     # wraps kubeseal; usage: seal.sh <namespace> <name> <value>
```

---

## Tasks

1. **Install ArgoCD** on the cluster (Ansible task or one-off `kubectl apply`; add to `site.yml`). Create `argocd` namespace; patch service to NodePort or use port-forward for UI.
2. **PostgreSQL StatefulSet**: `postgres:16-alpine`, `PersistentVolumeClaim` (local-path or hostPath StorageClass), resource limits (256 Mi memory), env from a Secret (`POSTGRES_PASSWORD`). Expose via `ClusterIP` service on port 5432.
3. **Migration bundle**: add a `Dockerfile.migrations` to `api-repo` that runs `dotnet ef migrations bundle`; build and push to GHCR alongside the API image in the CI pipeline.
4. **Migration PreSync Job** (`migration-job.yaml`): ArgoCD hook `argocd.argoproj.io/hook: PreSync`; runs migration bundle image; `restartPolicy: OnFailure`; uses DB_OWNER SealedSecret env var.
5. **API Helm chart**: `Deployment` with `runAsNonRoot: true`, `readOnlyRootFilesystem: true`, resource requests/limits (256Mi/512Mi), liveness probe (`/health/live`, initialDelay 10s), readiness probe (`/health/ready`, initialDelay 5s). `image.tag` is the only value that changes on each deploy.
6. **ArgoCD Applications**: `automated: { selfHeal: true, prune: true }`; point to `gitops-repo` chart paths; use the `app` namespace.
7. **Sealed Secrets**: document the `seal.sh` workflow; commit placeholder sealed secrets (encrypted with the cluster's public key) for `DB_OWNER`, `DB_APP_CONN`, and `JWT_KEY`.
8. **Seed Job**: a Kubernetes Job that runs `psql` to insert 2 demo users + 2 accounts after Postgres is ready.

---

## Definition of Done

- [ ] `argocd app sync postgres` deploys Postgres; pod is `Running`; `psql` from a debug pod connects.
- [ ] `argocd app sync banking-api` runs the migration PreSync Job first (EF migrations applied); then deploys the API pod.
- [ ] `kubectl logs <migration-job-pod>` shows "Done" with 0 errors.
- [ ] API pod passes liveness and readiness probes; `kubectl get pods -n app` shows `Running`.
- [ ] `curl https://<domain>/api/health/live` returns 200 (after Traefik + TLS in #08).
- [ ] No plaintext DB password or JWT key in any chart YAML or ArgoCD Application manifest.
- [ ] All image and chart versions are pinned.
- [ ] A rollback: `git revert` of the GitOps tag commit → ArgoCD syncs back to the previous API version.
