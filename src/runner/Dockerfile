# Self-hosted GitHub Actions runner image for Stocky.
# Extends myoung34/github-runner (Ubuntu 22.04) with Azure CLI so in-VNet
# CI steps can: az acr login, az containerapp *, az account get-access-token,
# az keyvault secret show, etc. — all routed over private endpoints.
#
# Auth mode: PAT  (ACCESS_TOKEN env var → KV secret-ref "github-runner-pat")
# Registration scope: repo  (REPO_OWNER + REPO_NAME → OdayAlUlabi/Stocky)
# See infra/modules/githubRunner.bicep for the KEDA event-scaler wiring.
FROM myoung34/github-runner:ubuntu-jammy

USER root

# Azure CLI — adds ~100 MB to the image; required for managed-identity login
# (az login --identity) and all Azure resource operations from runner steps.
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
       ca-certificates \
       curl \
       gnupg \
       lsb-release \
  && curl -sL https://aka.ms/InstallAzureCLIDeb | bash \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/*

USER runner
