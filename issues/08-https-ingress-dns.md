# Issue 08 — HTTPS, Traefik Ingress, Cloudflare DNS & cert-manager

| Field | Value |
|---|---|
| **Type** | 👤 Human-in-the-loop (domain, Cloudflare API token, live cluster required) |
| **Blocked By** | #05 (NLB DNS known); #06 (cluster running); Day-0: domain purchased and DNS moved to Cloudflare |
| **Tier** | Tier 1 (must) |

## Goal

Make the banking app reachable at `https://<domain>/` and `https://<domain>/api` with a valid Let's Encrypt certificate, through Traefik as the in-cluster ingress, behind the AWS NLB. All admin UIs remain private (no public route).

> [!NOTE]
> Cloudflare is in **DNS-only mode** (grey cloud) so encrypted TLS traffic reaches the cluster directly and cert-manager's HTTP-01 challenge can complete. Do NOT enable Cloudflare proxy mode.

---

## Target Files / Modules

```
gitops-repo/
  argocd/
    traefik-app.yaml            # ArgoCD Application for Traefik Helm chart
    cert-manager-app.yaml       # ArgoCD Application for cert-manager Helm chart

  charts/
    traefik/
      values.yaml               # pinned chart version; NodePort 30080/30443;
                                #   dashboard: false; non-root; resource limits
    cert-manager/
      values.yaml               # pinned chart; installCRDs: true
      clusterissuer.yaml        # ClusterIssuer: letsencrypt-prod (HTTP-01)

infra/
  scripts/
    update-dns.sh               # Cloudflare API: upsert A record → NLB IP (or CNAME)
                                # Uses CF_API_TOKEN env var (zone-scoped, DNS-edit only)

Makefile                        # extend: make dns-update (calls update-dns.sh)
```

---

## Tasks

1. **Traefik**: install via ArgoCD + Helm chart (not ingress-nginx which was archived Mar 2026). Configure: `service.type: NodePort`, port 30080 → HTTP, 30443 → HTTPS; `dashboard.enabled: false`; `securityContext.runAsNonRoot: true`; resource limits. Pin chart version.
2. **cert-manager**: install via ArgoCD + Helm chart (`installCRDs: true`). Create `ClusterIssuer` `letsencrypt-prod` with HTTP-01 solver on Traefik's NodePort.
3. **DNS update script** (`update-dns.sh`): uses Cloudflare API v4 to upsert an `A` record pointing `<domain>` → NLB IP. Reads `CF_API_TOKEN` and `CF_ZONE_ID` from env. Called by `make dns-update` on every `make up`.
4. **Ingress resources**: in the API and web Helm charts, `IngressClass: traefik`; TLS block referencing the cert-manager `Certificate` resource (or `cert-manager.io/cluster-issuer: letsencrypt-prod` annotation).
5. **Routing**: Traefik routes `<domain>/api` → `banking-api` Service; `<domain>/` → `banking-web` Service.
6. **Security headers**: Traefik Middleware for `Strict-Transport-Security`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`.
7. **Rate limiting** on `/api/auth/login`: Traefik `RateLimit` Middleware (e.g., 10 req/min per IP).

---

## Definition of Done

- [ ] `curl -I https://<domain>/api/health/live` returns HTTP 200 with a valid Let's Encrypt certificate (not self-signed).
- [ ] `curl -I https://<domain>/` returns the frontend or a 200/302 (once frontend is deployed in #10).
- [ ] Certificate is issued by Let's Encrypt; `openssl s_client` confirms chain is valid.
- [ ] Traefik dashboard is **not** reachable from the public internet; only via `kubectl port-forward`.
- [ ] Security headers present on every response (`Strict-Transport-Security`, `X-Frame-Options`, `X-Content-Type-Options`).
- [ ] Rate limit on `/api/auth/login`: >10 requests/min returns 429.
- [ ] `make dns-update` runs without error after every `make up`; DNS propagates within 60 s.
- [ ] All chart versions pinned; no `latest` tags.
