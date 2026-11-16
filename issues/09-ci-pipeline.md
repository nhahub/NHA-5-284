# Issue 09 — Jenkins CI Pipeline: Security Gates, Build, Scan & GitOps Tag Update

| Field | Value |
|---|---|
| **Type** | 👤 Human-in-the-loop (Jenkins + SonarQube on tools node required; GHCR token needed) |
| **Blocked By** | #06 (tools node with Jenkins running); #07 (GHCR image destination known); API code complete (#01–#04) |
| **Tier** | Tier 1 (must) |

## Goal

Write the `Jenkinsfile` for the API repo that implements the full security gate pipeline: unit tests → integration tests → gitleaks → SonarQube quality gate → Trivy dependency scan → Docker build (rootless BuildKit / Kaniko fallback) → Trivy image scan → GHCR push → GitOps tag update. Every gate blocks the merge if it fails.

> [!IMPORTANT]
> **Time-box rule:** If rootless BuildKit does not build images after 1 day of effort, switch to Kaniko (pinned by digest). The `Jenkinsfile` should have the Kaniko stage commented in and BuildKit commented out — not deleted.

---

## Target Files / Modules

```
Backend/                        # (api-repo)
  Jenkinsfile                   # Declarative pipeline; all stages below
  .sonarqube/
    sonar-project.properties    # projectKey, sources, tests, coverage report path

gitops-repo/
  charts/api/values.yaml        # image.tag field updated by pipeline bot commit

infra/
  jenkins/
    jenkins-agent-pod.yaml      # Kubernetes pod template: buildkit sidecar + dotnet + trivy containers
    kaniko-agent-pod.yaml       # fallback pod template: kaniko container
  scripts/
    update-gitops-tag.sh        # git clone gitops-repo; yq set image.tag; git commit + push
                                # Uses GITOPS_BOT_TOKEN (SealedSecret / Jenkins credential)
```

---

## Tasks

1. **Multibranch pipeline**: configure Jenkins to poll `api-repo` every 60 s; create pipeline from `Jenkinsfile`.
2. **Build pod** (Kubernetes agent): pod template with containers: `dotnet:8-sdk`, `aquasec/trivy`, `moby/buildkit` (rootless sidecar). NetworkPolicy allows only egress to GHCR and SonarQube.
3. **Pipeline stages** (all blocking unless noted):

   | Stage | Tool | Action |
   |---|---|---|
   | Checkout | git | Clone with shallow depth |
   | Build & Unit Tests | `dotnet test` | Run xUnit; publish JUnit XML; fail on any test failure |
   | Integration Tests | Testcontainers (Postgres sidecar) | Same `dotnet test` with integration tag; fail on any failure |
   | Secret Scan | gitleaks | `gitleaks detect --source .`; fail on any finding |
   | SAST + Quality Gate | SonarQube Scanner | `sonar-scanner`; poll quality gate; fail if `ERROR` |
   | Dependency Scan | Trivy | `trivy fs --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed .` |
   | Semgrep | semgrep (warn only) | `semgrep --config=auto`; archive report; never block |
   | Build Image | BuildKit / Kaniko | Build from `Dockerfile`; tag `ghcr.io/<org>/banking-api:<sha>` |
   | Image Scan | Trivy | `trivy image --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed` |
   | Push to GHCR | docker push | Authenticate with `GHCR_TOKEN` (Jenkins credential); push sha tag |
   | GitOps Update | `update-gitops-tag.sh` | Update `image.tag` in `gitops-repo`; commit + push as bot |

4. **Credentials**: `GHCR_TOKEN` and `GITOPS_BOT_TOKEN` stored as Jenkins credentials (not in Jenkinsfile). `SONAR_TOKEN` likewise.
5. **SonarQube quality gate**: configure project in SonarQube; set gate to ≥60% coverage on new code; `sonar-project.properties` points to the coverage XML from `dotnet test --collect:"XPlat Code Coverage"`.
6. **Post actions**: always archive test results (`junit '**/*.xml'`), Trivy reports, Semgrep report. On failure: send a build-status update to GitHub (`githubNotify`).
7. **Planted-secret demo path**: the demo PR introduces a hardcoded secret; gitleaks must catch it and the pipeline must fail at the gitleaks stage before image build.

---

## Definition of Done

- [ ] A passing PR triggers the pipeline; all stages green; image pushed to GHCR; GitOps tag updated; ArgoCD syncs.
- [ ] A PR with a hardcoded secret fails at the gitleaks stage; image is **never** built.
- [ ] A PR with a HIGH/CRITICAL CVE (unfixed) fails at the dependency scan stage.
- [ ] SonarQube quality gate failure blocks the pipeline before image push.
- [ ] Integration test failure blocks the pipeline.
- [ ] The pushed image is tagged with the commit SHA (not `latest`).
- [ ] No credential appears in `Jenkinsfile`, `sonar-project.properties`, or any committed file.
- [ ] `kubectl get pods -n jenkins-agents` shows build pods during a run and `Completed`/`Deleted` after.
