# Issue 10 — Observability: Prometheus, Grafana, Alerts & Loki

| Field | Value |
|---|---|
| **Type** | 🤖 AFK Agent (chart authoring) + 👤 Human-in-the-loop (live cluster sync) |
| **Blocked By** | #07 (ArgoCD operational); #08 (API reachable so `/metrics` can be scraped) |
| **Tier** | Tier 2 (important) |

## Goal

Deploy `kube-prometheus-stack` via ArgoCD, wire the API's `/metrics` endpoint into Prometheus, create three Grafana dashboards (cluster health, API, database), configure four Alertmanager alerts, and optionally add Loki for log aggregation. All within the memory budget of the nodes (explicit limits on every component).

> [!WARNING]
> Loki is the **first component to drop** if nodes are running out of memory. Keep Loki's Helm values in a separate `values-loki.yaml` and its ArgoCD Application in a separate file so it can be disabled without touching the monitoring stack.

---

## Target Files / Modules

```
gitops-repo/
  argocd/
    monitoring-app.yaml         # ArgoCD Application: kube-prometheus-stack
    loki-app.yaml               # ArgoCD Application: loki-stack (optional, disable first)

  charts/
    monitoring/
      values.yaml               # kube-prometheus-stack values:
                                #   prometheus.retention: 2d
                                #   prometheus.resources: limits 1Gi
                                #   grafana.resources: limits 256Mi
                                #   alertmanager.resources: limits 128Mi
                                #   grafana.adminPassword: ref to SealedSecret
      dashboards/
        cluster-health.json     # node CPU, memory, pod count
        api.json                # request rate, error rate, p99 latency (from /metrics)
        postgres.json           # connections, query latency (postgres_exporter)
      alerting/
        alerts.yaml             # PrometheusRule: 4 required alerts (see below)
      sealed-secret-grafana.yaml# SealedSecret: grafana admin password

    loki/
      values.yaml               # loki-stack; singleBinary mode; retention 24h; limits 512Mi

  charts/api/
    templates/
      servicemonitor.yaml       # ServiceMonitor: scrape /metrics on port 8080
```

---

## Tasks

1. **kube-prometheus-stack**: install via ArgoCD + Helm (pinned version). Set explicit memory limits on every sub-chart. Retention 2 days. Grafana admin password from SealedSecret. Disable node-exporter on control-plane (taint).
2. **API metrics**: the API exposes `/metrics` via `prometheus-net` (add NuGet package in issue #01 or here). Create a `ServiceMonitor` CRD resource pointing to the API service.
3. **Postgres exporter**: add `postgres-exporter` as a sidecar or standalone deployment; scrape via `ServiceMonitor`.
4. **Three Grafana dashboards** (JSON provisioned via ConfigMap):
   - `cluster-health.json`: node CPU %, node memory %, pod count per node.
   - `api.json`: request rate (by route), error rate (5xx/total), p99 latency (histogram).
   - `postgres.json`: active connections, transaction rate, query latency.
5. **Four Alertmanager alerts** (`PrometheusRule`):
   - `PodCrashLooping`: `kube_pod_container_status_restarts_total` rate > 1/5m for 5m.
   - `NodeNotReady`: `kube_node_status_condition{condition="Ready",status="true"} == 0` for 2m.
   - `ArgoCDSyncFailing`: `argocd_app_info{sync_status!="Synced"} == 1` for 5m.
   - `JenkinsBuildFailing`: custom metric or Jenkins Prometheus plugin; alert if last build failed.
   - Configure receiver (email or Slack — resolve in Milestone 0 check #7).
6. **Loki** (optional, skip if memory tight): `loki-stack` Helm chart; configure Promtail to tail pod logs; add Loki as a Grafana datasource. Filter pattern for failed logins: `{app="banking-api"} |= "login failed"`.

---

## Definition of Done

- [ ] `kubectl get pods -n monitoring` shows all Prometheus, Grafana, and Alertmanager pods `Running`.
- [ ] Grafana UI accessible via `kubectl port-forward`; admin login works.
- [ ] All three dashboards load with live data (non-zero panels).
- [ ] API's `/metrics` endpoint is being scraped: `curl prometheus:9090/api/v1/query?query=http_requests_received_total` returns data.
- [ ] All four alert rules appear in Alertmanager UI; firing a test condition (e.g., scale a pod to 0) triggers the alert within 10 minutes.
- [ ] Total memory used by the monitoring namespace is within node capacity (verify with `kubectl top pods -n monitoring`).
- [ ] No Grafana admin password in plaintext in any committed file.
