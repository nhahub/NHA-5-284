# Issue 11 — Security Hardening: NetworkPolicies, Pod Security & Sealed Secrets Restore Test

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent (manifest authoring) + 👤 Human-in-the-loop (cluster apply & rebuild test) |
| **Blocked By** | #07 (namespaces and workloads deployed); #08 (Traefik routing known) |
| **Tier** | Tier 2 (important) |

## Goal

Harden the running cluster with default-deny NetworkPolicies per namespace, pod security hardening (non-root, read-only FS, resource limits), and a verified Sealed Secrets sealing-key backup + restore. This issue makes the cluster match the security requirements in PRD §7.

---

## Target Files / Modules

```
gitops-repo/
  security/
    networkpolicies/
      app-ns-netpol.yaml        # default-deny ingress/egress in `app` namespace;
                                #   explicit allow: traefik→api:8080, api→postgres:5432,
                                #   api→kube-dns:53, monitoring→api:8080(metrics)
      monitoring-ns-netpol.yaml # default-deny; allow: prometheus→scrape targets,
                                #   grafana→prometheus, alertmanager outbound
      argocd-ns-netpol.yaml     # allow: argocd→api-server:6443, argocd→gitops-repo (egress)
      jenkins-agents-netpol.yaml# allow: agents→SonarQube, agents→GHCR (egress only)

  charts/api/
    values.yaml                 # (update) securityContext: runAsNonRoot: true,
                                #   readOnlyRootFilesystem: true, allowPrivilegeEscalation: false,
                                #   capabilities.drop: [ALL]

infra/
  scripts/
    backup-sealing-key.sh       # kubectl get secret -n kube-system sealed-secrets-key -o yaml
                                # > sealed-secrets-key.yaml  (store in password manager, NOT Git)
    restore-sealing-key.sh      # kubectl apply -f sealed-secrets-key.yaml
                                # kubectl rollout restart -n kube-system deploy/sealed-secrets
```

---

## Tasks

1. **Default-deny NetworkPolicy per namespace**: every namespace (`app`, `monitoring`, `argocd`, `cert-manager`, `traefik`, `jenkins-agents`) gets a `NetworkPolicy` that denies all ingress and egress by default.
2. **Explicit allow policies** (additive rules on top of default-deny):
   - `app`: ingress from `traefik` namespace on port 8080; egress to `postgres-svc` port 5432; egress to `kube-dns` port 53 (UDP); ingress from `monitoring` namespace on port 8080.
   - `traefik`: ingress from NLB CIDR on 30080/30443; egress to `app` namespace.
   - `monitoring`: egress to all namespaces on scrape ports; egress to alert receivers.
   - `argocd`: egress to k8s API server; egress to GitHub (HTTPS) for gitops-repo.
   - `jenkins-agents`: egress to SonarQube IP:9000; egress to GHCR; no ingress.
3. **Pod security context** in API Helm chart:
   ```yaml
   securityContext:
     runAsNonRoot: true
     runAsUser: 1000
     readOnlyRootFilesystem: true
     allowPrivilegeEscalation: false
     capabilities:
       drop: [ALL]
   ```
   Add an `emptyDir` volume mounted at `/tmp` to allow the app to write temp files.
4. **Resource limits**: confirm every Deployment/StatefulSet/Job in all namespaces has explicit `requests` and `limits` for CPU and memory (no unlimited pods).
5. **Sealed Secrets sealing-key backup**: run `backup-sealing-key.sh` and store the output in the password manager. Document the restore procedure in the runbook.
6. **Restore test**: on a test rebuild (`make up` fresh cluster), run `restore-sealing-key.sh` before deploying the Sealed Secrets controller, then verify ArgoCD can decrypt the SealedSecrets and the API connects to Postgres with the decrypted credentials.

---

## Definition of Done

- [ ] `kubectl exec -n app <api-pod> -- id` shows `uid=1000` (non-root).
- [ ] `kubectl exec -n app <api-pod> -- touch /test` fails with "Read-only file system".
- [ ] `kubectl exec -n app <api-pod> -- wget http://google.com` times out (egress blocked by NetworkPolicy).
- [ ] API pod can still reach Postgres: `kubectl exec <api-pod> -- nc -zv postgres-svc 5432` succeeds.
- [ ] Monitoring scrape still works after NetworkPolicy apply: Prometheus targets page shows API `UP`.
- [ ] A fresh `make up` with the sealing key restored from the password manager results in all SealedSecrets decrypting successfully within 2 minutes.
- [ ] `kubectl describe pod <any-pod> -n app` shows no pod with `privileged: true` or missing resource limits.
- [ ] `checkov -d gitops-repo/security` and `checkov -d gitops-repo/charts` pass on critical rules.
