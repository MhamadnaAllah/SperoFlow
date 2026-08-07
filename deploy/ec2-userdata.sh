#!/bin/bash
set -euo pipefail
exec > >(tee /var/log/speroflow-bootstrap.log) 2>&1
echo "=== SperoFlow bootstrap start $(date -u) ==="

REGION="us-east-1"
REPO_DIR="/opt/speroflow"

# --- 1. Install Docker + git + jq ---
dnf update -y
dnf install -y docker git jq
systemctl enable --now docker
usermod -aG docker ec2-user

# --- 2. Docker Compose plugin ---
mkdir -p /usr/local/lib/docker/cli-plugins
curl -fsSL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64" -o /usr/local/lib/docker/cli-plugins/docker-compose
chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
# Docker Buildx (compose v5 "build" requires buildx >= 0.17)
curl -fsSL https://github.com/docker/buildx/releases/download/v0.30.1/buildx-v0.30.1.linux-amd64 -o /usr/local/lib/docker/cli-plugins/docker-buildx
chmod +x /usr/local/lib/docker/cli-plugins/docker-buildx
docker compose version
docker buildx version

# --- 3. Get application source (compose files + Dockerfiles + Caddyfile) ---
if [ ! -d "$REPO_DIR/.git" ]; then
  git clone --depth 1 --branch main https://github.com/MhamadnaAllah/SperoFlow.git "$REPO_DIR"
else
  git -C "$REPO_DIR" pull --ff-only || true
fi
cd "$REPO_DIR"

# --- 4. Pull secrets from Secrets Manager into ./infrastructure/secrets ---
mkdir -p infrastructure/secrets
fetch() {
  local id="$1" out="$2" kind="${3:-text}"
  if [ "$kind" = "binary" ]; then
    aws secretsmanager get-secret-value --secret-id "speroflow/prod/$id" --region "$REGION" --query SecretBinary --output text | base64 -d > "infrastructure/secrets/$out"
  else
    aws secretsmanager get-secret-value --secret-id "speroflow/prod/$id" --region "$REGION" --query SecretString --output text > "infrastructure/secrets/$out"
  fi
  # 0644 (not 0600): secret files are bind-mounted into non-root containers
  # that must be able to read them; the dir itself stays private on the host.
  chmod 644 "infrastructure/secrets/$out"
}
fetch postgres_password postgres_password
fetch redis_password redis_password
fetch neo4j_password neo4j_password
fetch minio_access_key minio_access_key
fetch minio_secret_key minio_secret_key
fetch service_jwt_private_key service_jwt_private_key
fetch service_jwt_public_key service_jwt_public_key
fetch smtp_password smtp_password
fetch admin_bootstrap_token admin_bootstrap_token
fetch oidc_signing_certificate oidc_signing_certificate binary
fetch oidc_signing_certificate_password oidc_signing_certificate_password
fetch oidc_encryption_certificate oidc_encryption_certificate binary
fetch oidc_encryption_certificate_password oidc_encryption_certificate_password
fetch knowledge_neo4j_reader_password knowledge_neo4j_reader_password
fetch knowledge_grant_public_key knowledge_grant_public_key
fetch knowledge_grant_private_key knowledge_grant_private_key
fetch knowledge_service_jwt_private_key knowledge_service_jwt_private_key
fetch knowledge_service_jwt_public_key knowledge_service_jwt_public_key
fetch knowledge_portal_data_protection_certificate knowledge_portal_data_protection_certificate binary
fetch knowledge_portal_data_protection_certificate_password knowledge_portal_data_protection_certificate_password

# --- 5. Production env for Bedrock routing ---
cat > .env <<EOF
BEDROCK_REGION=$REGION
BEDROCK_MODEL_ID=google.gemma-4-31b
BEDROCK_ROUTER_MODEL_ID=google.gemma-4-31b
EMBEDDING_MODEL_ID=cohere.embed-v4:0
RELAX_ANTIFORGERY_SECURE_COOKIE=true
EOF

# --- 6. Production Caddyfile (Caddy terminates TLS for the 4 hostnames) ---
cp deploy/Caddyfile.prod infrastructure/caddy/Caddyfile

# --- 7. Build images from source on the host and start the stack ---
docker compose -f compose.yaml -f compose.prod.yaml build
docker compose -f compose.yaml -f compose.prod.yaml up -d

echo "=== SperoFlow bootstrap complete $(date -u) ==="
docker compose -f compose.yaml -f compose.prod.yaml ps
