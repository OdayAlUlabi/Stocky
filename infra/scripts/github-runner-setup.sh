#!/bin/bash
# Self-hosted GitHub Actions runner setup script for Azure VM.
#
# Reads all configuration from environment variables set by the
# Bicep CustomScript extension commandToExecute before running this script:
#
#   GH_OWNER     - GitHub organisation / user name
#   GH_REPO      - repository name (empty = org-level runner)
#   GH_LABELS    - comma-separated extra labels (appended to "self-hosted,linux")
#   KV_URI       - Key Vault base URI, e.g. https://kv-xxx.vault.azure.net/
#   USE_APP_AUTH - "true" for GitHub App token, anything else for PAT
#   APP_ID       - GitHub App id (required when USE_APP_AUTH=true)
#   INSTALL_ID   - GitHub App installation id (required when USE_APP_AUTH=true)
#   CRED_NAME    - Key Vault secret name that holds the credential
#
# The script:
#   1. Installs prerequisites (curl, jq, openssl, libicu-dev, git, Docker, Azure CLI)
#   2. Fetches the runner credential from Key Vault via managed identity (IMDS)
#   3. Obtains a GitHub runner registration token
#   4. Downloads, configures and starts the runner as a systemd service
#
# Re-runs are safe: 'config.sh --replace' re-registers without manual cleanup.

set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

RUNNER_VERSION="2.323.0"
RUNNER_HOME="/opt/github-runner"
RUNNER_USER="ghrunner"

log() { echo "[runner-setup] $(date -u '+%H:%M:%S') $*"; }

# ---------------------------------------------------------------------------
# 1. Prerequisites
# ---------------------------------------------------------------------------
# azure.archive.ubuntu.com is HTTP-only (no HTTPS server on port 443) and port 80
# is blocked by network policy.  Switch to archive.ubuntu.com which supports HTTPS.
log "Switching apt sources from azure mirror to archive.ubuntu.com (HTTPS)..."
sed -i 's|http://azure.archive.ubuntu.com/ubuntu|https://archive.ubuntu.com/ubuntu|g' /etc/apt/sources.list
sed -i 's|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list
sed -i 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g' /etc/apt/sources.list
find /etc/apt/sources.list.d/ -name '*.list' \
  -exec sed -i 's|http://azure\.archive\.ubuntu\.com/ubuntu|https://archive.ubuntu.com/ubuntu|g' {} \; 2>/dev/null || true
find /etc/apt/sources.list.d/ -name '*.list' \
  -exec sed -i 's|http://|https://|g' {} \; 2>/dev/null || true

log "Disabling apt TLS peer verification (firewall may do TLS inspection)..."
cat > /etc/apt/apt.conf.d/99noverify <<'APTEOF'
Acquire::https::Verify-Peer "false";
Acquire::https::Verify-Host "false";
APTEOF

# Wrap curl so that all invocations (including scripts piped to bash) skip TLS validation.
mkdir -p /usr/local/sbin/curlwrap
cat > /usr/local/sbin/curlwrap/curl <<'CURLWRAP'
#!/bin/bash
exec /usr/bin/curl -k "$@"
CURLWRAP
chmod +x /usr/local/sbin/curlwrap/curl
export PATH="/usr/local/sbin/curlwrap:${PATH}"

log "Disabling automatic apt update timers and services..."
systemctl disable apt-daily.timer apt-daily-upgrade.timer 2>/dev/null || true
systemctl stop apt-daily.timer apt-daily-upgrade.timer 2>/dev/null || true
systemctl stop unattended-upgrades apt-daily.service apt-daily-upgrade.service 2>/dev/null || true

log "Killing any running apt/dpkg/unattended-upgrades processes..."
pkill -TERM -f unattended-upgrades 2>/dev/null || true
sleep 3
pkill -KILL -f unattended-upgrades 2>/dev/null || true
pkill -KILL -x apt-get 2>/dev/null || true
sleep 2

log "Waiting for apt lock files to be released..."
for _lock in /var/lib/dpkg/lock-frontend /var/lib/dpkg/lock /var/lib/apt/lists/lock; do
  _waited=0
  while fuser "${_lock}" >/dev/null 2>&1; do
    log "Lock ${_lock} still held, waiting..."
    sleep 5
    _waited=$((_waited + 5))
    [ "${_waited}" -ge 300 ] && break
  done
done

log "Clearing apt lists cache (so new mirror URLs are used on next update)..."
rm -rf /var/lib/apt/lists/*

log "Updating apt (failure here is non-fatal)..."
apt-get update -qq 2>/dev/null || log "apt-get update failed; will rely on pre-installed packages + static binaries"

# curl, git, openssl, gnupg, ca-certificates, lsb-release are pre-installed on Ubuntu 22.04 LTS.
# jq may need to be fetched; libicu-dev (dev headers) is NOT needed.
log "Installing prerequisites (best-effort)..."
apt-get install -y --no-install-recommends \
  curl jq openssl git ca-certificates gnupg lsb-release 2>/dev/null || \
  log "apt-get install had errors; will use static binaries where needed"

# jq static binary fallback — works even when apt mirrors are unreachable
if ! command -v jq >/dev/null 2>&1; then
  log "jq not available via apt; downloading static binary from GitHub..."
  curl -kfsSL -o /usr/local/bin/jq \
    "https://github.com/jqlang/jq/releases/download/jq-1.7.1/jq-linux-amd64"
  chmod +x /usr/local/bin/jq
fi

# Verify critical tools are present
command -v curl   >/dev/null || { log "ERROR: curl missing"; exit 1; }
command -v jq     >/dev/null || { log "ERROR: jq missing (apt and static download both failed)"; exit 1; }
command -v openssl>/dev/null || { log "ERROR: openssl missing"; exit 1; }

# Docker — best-effort; network may block download.docker.com
if ! command -v docker &>/dev/null; then
  log "Installing Docker (best-effort)..."
  (
    set +euo pipefail
    install -m 0755 -d /etc/apt/keyrings
    curl -fksSL https://download.docker.com/linux/ubuntu/gpg \
      | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    chmod a+r /etc/apt/keyrings/docker.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
      https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" \
      > /etc/apt/sources.list.d/docker.list
    apt-get update -qq
    apt-get install -y --no-install-recommends docker-ce docker-ce-cli containerd.io
    systemctl enable docker && systemctl start docker
  ) && log "Docker installed" || log "Warning: Docker installation failed — workflows requiring Docker may not work"
fi

# Azure CLI — best-effort
if ! command -v az &>/dev/null; then
  log "Installing Azure CLI (best-effort)..."
  (
    set +euo pipefail
    curl -ksSL https://aka.ms/InstallAzureCLIDeb | bash
  ) && log "Azure CLI installed" || log "Warning: Azure CLI installation failed — workflows using 'az' may not work"
fi

# ---------------------------------------------------------------------------
# 2. Fetch credential from Key Vault via managed identity
# ---------------------------------------------------------------------------
log "Acquiring managed identity token for Key Vault..."
MI_TOKEN=$(curl -sf \
  -H "Metadata: true" \
  "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://vault.azure.net" \
  | jq -r '.access_token')

log "Fetching credential '${CRED_NAME}' from Key Vault..."
CRED=$(curl -sf \
  -H "Authorization: Bearer ${MI_TOKEN}" \
  "${KV_URI}secrets/${CRED_NAME}?api-version=7.4" \
  | jq -r '.value')

# ---------------------------------------------------------------------------
# 3. Get GitHub runner registration token
# ---------------------------------------------------------------------------
if [ "${USE_APP_AUTH}" = "true" ]; then
  log "Generating GitHub App JWT (app id=${APP_ID})..."
  NOW=$(date +%s)
  EXP=$((NOW + 540))   # 9-minute window; GitHub max is 10 min

  HEADER=$(printf '{"alg":"RS256","typ":"JWT"}' \
    | openssl base64 -A | tr '+/' '-_' | tr -d '=')
  PAYLOAD=$(printf '{"iat":%s,"exp":%s,"iss":"%s"}' "${NOW}" "${EXP}" "${APP_ID}" \
    | openssl base64 -A | tr '+/' '-_' | tr -d '=')

  KEY_FILE=$(mktemp)
  chmod 600 "${KEY_FILE}"
  printf '%s' "${CRED}" > "${KEY_FILE}"
  SIG=$(printf '%s.%s' "${HEADER}" "${PAYLOAD}" \
    | openssl dgst -sha256 -sign "${KEY_FILE}" \
    | openssl base64 -A | tr '+/' '-_' | tr -d '=')
  rm -f "${KEY_FILE}"

  JWT="${HEADER}.${PAYLOAD}.${SIG}"

  log "Exchanging JWT for installation token (installation=${INSTALL_ID})..."
  INSTALL_TOKEN=$(curl -sf \
    -X POST \
    -H "Authorization: Bearer ${JWT}" \
    -H "Accept: application/vnd.github+json" \
    "https://api.github.com/app/installations/${INSTALL_ID}/access_tokens" \
    | jq -r '.token')

  AUTH_HEADER="token ${INSTALL_TOKEN}"
else
  log "Using PAT auth..."
  AUTH_HEADER="token ${CRED}"
fi

if [ -z "${GH_REPO:-}" ]; then
  log "Getting org-scope registration token..."
  REG_TOKEN=$(curl -sf -X POST \
    -H "Authorization: ${AUTH_HEADER}" \
    -H "Accept: application/vnd.github+json" \
    "https://api.github.com/orgs/${GH_OWNER}/actions/runners/registration-token" \
    | jq -r '.token')
  RUNNER_URL="https://github.com/${GH_OWNER}"
else
  log "Getting repo-scope registration token..."
  REG_TOKEN=$(curl -sf -X POST \
    -H "Authorization: ${AUTH_HEADER}" \
    -H "Accept: application/vnd.github+json" \
    "https://api.github.com/repos/${GH_OWNER}/${GH_REPO}/actions/runners/registration-token" \
    | jq -r '.token')
  RUNNER_URL="https://github.com/${GH_OWNER}/${GH_REPO}"
fi

# ---------------------------------------------------------------------------
# 4. Download and configure the runner
# ---------------------------------------------------------------------------
id "${RUNNER_USER}" &>/dev/null || useradd -m -s /bin/bash "${RUNNER_USER}"
# Add runner user to docker group so workflows can run Docker commands
usermod -aG docker "${RUNNER_USER}" 2>/dev/null || true

mkdir -p "${RUNNER_HOME}"
cd "${RUNNER_HOME}"

if [ ! -f ./config.sh ]; then
  log "Downloading runner v${RUNNER_VERSION}..."
  curl -kLo actions-runner.tar.gz \
    "https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz"
  tar xzf actions-runner.tar.gz
  rm -f actions-runner.tar.gz
fi

chown -R "${RUNNER_USER}:${RUNNER_USER}" "${RUNNER_HOME}"

RUNNER_NAME="vm-$(hostname | tr '[:upper:]' '[:lower:]')"
log "Configuring runner '${RUNNER_NAME}' -> ${RUNNER_URL}..."
sudo -u "${RUNNER_USER}" ./config.sh \
  --url "${RUNNER_URL}" \
  --token "${REG_TOKEN}" \
  --name "${RUNNER_NAME}" \
  --labels "self-hosted,linux,${GH_LABELS}" \
  --work "_work" \
  --unattended \
  --replace

# ---------------------------------------------------------------------------
# 5. Install as systemd service and start
# ---------------------------------------------------------------------------
log "Installing and starting runner service..."
./svc.sh install "${RUNNER_USER}"
./svc.sh start

log "Runner '${RUNNER_NAME}' is active. Labels: self-hosted,linux,${GH_LABELS}"
