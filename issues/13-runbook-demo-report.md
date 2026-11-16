# Issue 13 — Runbook, Demo Rehearsal & Final Report

| Field | Value |
|---|---|
| **Type** | 👤 Human-in-the-loop (requires a fully working platform) |
| **Blocked By** | #09 (pipeline), #10 (monitoring), #11 (security hardening), #12 (frontend) — all Tier 1 and Tier 2 issues complete |
| **Tier** | Tier 2 (important) / deadline gate |

## Goal

Produce the deliverables that make the platform a gradable, reproducible project: an operations runbook, an architecture diagram, two timed rebuild rehearsals, the scripted live demo (including the blocked PR, the rollback, and the monitoring view), and the written report. Feature freeze 3 days before the deadline.

---

## Target Files / Modules

```
docs/
  runbook.md                    # Build, operate, back up, restore, roll back, tear down
  architecture-diagram.png      # or .svg — generated from draw.io or Mermaid
  report.md                     # Written report (see tasks)

README.md                       # Root repo README: quick start, links to runbook + report

plans/
  demo-script.md                # Step-by-step scripted demo (mirrors PRD §3.3)
  rebuild-timings.md            # Recorded timings for ≥2 full rebuilds

recordings/
  demo-backup.mp4               # Recorded backup of one full successful demo run
```

---

## Tasks

1. **Runbook** (`docs/runbook.md`): cover each operation with exact commands:
   - **Build**: `make up` sequence (bootstrap → `tf-apply` → `ansible-cluster` → `ansible-tools` → GitOps bootstrap → `dns-update`)
   - **Operate**: how to access Jenkins, ArgoCD, Grafana, SonarQube (SSM / port-forward)
   - **Sealed Secrets restore**: exact steps for a fresh cluster
   - **Back up**: how to trigger and verify the Postgres backup CronJob; where files go
   - **Roll back**: `git revert <gitops-commit>` + verify ArgoCD restores previous version + confirm migration backward compatibility
   - **Tear down**: `make tf-destroy` + post-destroy checklist (EC2, EIP, NLB — all terminated)

2. **Architecture diagram**: one diagram showing: developer laptop → GitHub → Jenkins (tools node) → GHCR → gitops-repo → ArgoCD → Kubernetes cluster (namespaces: app, monitoring, traefik, argocd, cert-manager) → NLB → Cloudflare → browser. Mark security boundaries.

3. **Demo script** (`plans/demo-script.md`): detailed, step-by-step script mirroring PRD §3.3:
   - Exact commands/clicks for each of the 8 demo steps
   - Fallback action for each step if something is live-unstable
   - Expected output/screenshot description

4. **Rebuild rehearsals** (≥2 runs):
   - Start from `make tf-destroy` (empty AWS state)
   - Time `make up` end-to-end; record in `plans/rebuild-timings.md`
   - Verify platform works after rebuild (health check, login, transfer)
   - Second run rehearsed with the sealing-key restore path

5. **Recorded backup demo** (`recordings/demo-backup.mp4`): record one full successful demo run (10–15 min) following `plans/demo-script.md`. Store outside Git (too large); link from README.

6. **Written report** (`docs/report.md`): sections —
   - Project overview and goals
   - Architecture decisions (why kubeadm/K3s fallback, Traefik over ingress-nginx, polling over webhooks, etc.)
   - Security model summary (each PRD §7 requirement → how met)
   - Observability setup
   - What I would add in production (one sentence per PRD §14 non-goal)
   - Lessons learned

7. **Root README**: quick-start (prerequisites + `make up`), links to runbook, report, and demo recording.

---

## Definition of Done

- [ ] `docs/runbook.md` covers all 6 operations with exact commands; a team member who was not present can follow it.
- [ ] Architecture diagram is committed and linked from README.
- [ ] Two timed rebuild timings are recorded in `plans/rebuild-timings.md`; both runs succeed.
- [ ] The demo script runs end-to-end without deviation: blocked PR, fix, merge, build, ArgoCD sync, app use, Grafana view, rollback.
- [ ] A backup recording of the demo exists and is accessible (linked from README).
- [ ] `docs/report.md` addresses every bullet; non-goals section has one sentence per item.
- [ ] `README.md` at repo root is complete and accurate.
- [ ] Feature freeze respected: no new features after the freeze date; only runbook/report/demo polish.
