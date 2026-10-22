#!/usr/bin/env bash
# ==============================================================================
# Velox Cloud — AWS Lightsail 1-Click Provisioning & Deployment Script
# Targets: Ubuntu 24.04 / 22.04 LTS on AWS Lightsail
# ==============================================================================
set -euo pipefail

echo "=========================================================="
echo "   🚀 Starting Velox Cloud Deployment on AWS Lightsail    "
echo "=========================================================="

# 1. Ensure Root Privileges
if [ "$EUID" -ne 0 ]; then
  echo "[-] Please run as root (use sudo bash deploy-lightsail.sh)"
  exit 1
fi

# 2. Update System Packages
echo "[1/6] Updating system packages..."
apt-get update -y
DEBIAN_FRONTEND=noninteractive apt-get upgrade -y

# 3. Install Docker & Docker Compose
echo "[2/6] Installing Docker & Docker Compose..."
apt-get install -y docker.io docker-compose-v2 curl ufw git

systemctl enable --now docker

# 4. Configure Firewall (UFW)
echo "[3/6] Configuring firewall rules (Ports 22, 80, 443)..."
ufw allow 22/tcp || true
ufw allow 80/tcp || true
ufw allow 443/tcp || true
ufw --force enable || true

# 5. Determine Public IP
PUBLIC_IP=$(curl -s http://checkip.amazonaws.com || curl -s https://api.ipify.org || echo "YOUR-SERVER-IP")
echo "[+] Detected Lightsail Public IP: ${PUBLIC_IP}"

# 6. Build and Run Containers via Docker Compose
echo "[4/6] Building and starting Velox Cloud containers..."
docker compose down --remove-orphans || true
docker compose up -d --build

# 7. Verification & Health Check
echo "[5/6] Waiting for Velox Cloud API to become ready..."
ATTEMPTS=0
MAX_ATTEMPTS=20
HEALTHY=0

while [ $ATTEMPTS -lt $MAX_ATTEMPTS ]; do
  HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost/health || true)
  if [ "$HTTP_STATUS" = "200" ]; then
    HEALTHY=1
    break
  fi
  echo "    Waiting for API container (Attempt $((ATTEMPTS+1))/$MAX_ATTEMPTS)..."
  sleep 2
  ATTEMPTS=$((ATTEMPTS+1))
done

echo "=========================================================="
if [ $HEALTHY -eq 1 ]; then
  echo "✅ [VLX-OK-200] Velox Cloud API is LIVE on AWS Lightsail!"
  echo ""
  echo "Public API URL:      http://${PUBLIC_IP}"
  echo "Health Check:        http://${PUBLIC_IP}/health"
  echo "Bench Sync:          http://${PUBLIC_IP}/api/v1/bench/sync"
  echo "License Validation:  http://${PUBLIC_IP}/api/v1/license/validate"
  echo ""
  echo "Default Alpha License: VLX-ALPHA-TEST-2026"
  echo ""
  echo "To connect VeloxBench client:"
  echo "1. Open config.json beside VeloxBench.exe"
  echo "2. Set: \"ApiUrl\": \"http://${PUBLIC_IP}\""
  echo "=========================================================="
else
  echo "⚠️ Warning: Health check did not return 200 within 40 seconds."
  echo "Check container logs with: docker compose logs -f"
  echo "=========================================================="
fi
