# Issue 12 — Frontend Deployment via Helm & ArgoCD

| Field | Value |
|---|---|
| **Type** | 👤 Human-in-the-loop (depends on teammate's Docker image being available on GHCR) |
| **Blocked By** | #08 (Traefik + HTTPS routing); #09 (CI pipeline pattern reusable for web-repo) |
| **Tier** | Tier 2 (important) |

## Goal

Deploy the frontend image (built by the teammate) through the same GitOps pattern as the API: a Helm chart in `gitops-repo`, an ArgoCD Application, Traefik Ingress routing `/` to the frontend, and a CI pipeline (`Jenkinsfile`) in `web-repo` that builds, scans, pushes to GHCR, and updates the GitOps tag.

> [!NOTE]
> The owner does **not** write frontend code. This issue is only about the deployment infrastructure: Dockerfile validation, Helm chart, Ingress routing, and pipeline. If the teammate's image is not ready, deploy a placeholder `nginx:alpine` image to prove the pipeline works.

---

## Target Files / Modules

```
gitops-repo/
  charts/web/
    Chart.yaml
    values.yaml                 # image.repository, image.tag, replicas=1,
                                #   resources (128Mi/256Mi), ingress.host
    templates/
      deployment.yaml           # non-root, readOnlyRootFilesystem, resource limits
      service.yaml              # ClusterIP
      ingress.yaml              # Traefik: host=<domain>, path=/ (prefix)
  argocd/
    banking-web-app.yaml        # ArgoCD Application (already stubbed in #07)

web-repo/                       # teammate's repo — owner adds infra files only
  Jenkinsfile                   # mirrors api Jenkinsfile: gitleaks, Trivy FS,
                                #   docker build, Trivy image, GHCR push, GitOps update
  Dockerfile                    # validate it exists, non-root, no secrets in layers
```

---

## Tasks

1. **Complete `charts/web/`**: fill in `deployment.yaml` with the same security hardening as the API chart (non-root, read-only FS, resource limits, liveness/readiness probes on the frontend's health path or `/`).
2. **Traefik Ingress**: route `<domain>/` → `banking-web` Service; route `<domain>/api` → `banking-api` Service (from #08 — confirm no conflict). Frontend uses relative `/api/...` calls so no CORS config is needed.
3. **ArgoCD Application**: finalize `banking-web-app.yaml` with auto-sync and self-heal; point to `gitops-repo/charts/web`.
4. **web-repo Jenkinsfile**: simplified version of the API pipeline — no integration tests, no SonarQube (or minimal); must include gitleaks, Trivy FS, Docker build, Trivy image scan, GHCR push, GitOps tag update.
5. **Dockerfile audit**: verify the teammate's Dockerfile runs as non-root; if not, propose a wrapper stage. Scan with `trivy image` and confirm no HIGH/CRITICAL with a fix.
6. **Smoke test**: after deployment, `curl https://<domain>/` returns 200 and `curl https://<domain>/api/health/live` returns 200 (both routes working simultaneously).

---

## Definition of Done

- [ ] `argocd app sync banking-web` deploys the frontend pod; pod is `Running`.
- [ ] `curl https://<domain>/` returns HTTP 200; frontend HTML is served.
- [ ] `curl https://<domain>/api/health/live` still returns 200 (no routing conflict).
- [ ] Frontend pod runs as non-root; `kubectl exec <web-pod> -- id` shows non-root uid.
- [ ] A commit to `web-repo/main` triggers the Jenkins pipeline; image pushed to GHCR; ArgoCD syncs the new tag.
- [ ] No secrets in Jenkinsfile or Dockerfile.
