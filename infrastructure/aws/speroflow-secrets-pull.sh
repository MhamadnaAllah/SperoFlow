#!/usr/bin/env bash
# Linux EC2 helper: materialize Compose secrets from Secrets Manager without PowerShell.
# Requires: aws CLI v2, jq, instance role with secretsmanager:GetSecretValue.
set -euo pipefail

ENV_NAME="${SPEROFLOW_SECRETS_ENV:-prod}"
REGION="${AWS_REGION:-${AWS_DEFAULT_REGION:-}}"
ROOT="${SPEROFLOW_ROOT:-/opt/speroflow}"
SECRETS_DIR="${SPEROFLOW_SECRETS_DIR:-$ROOT/infrastructure/secrets}"
CATALOG="${SPEROFLOW_SECRETS_CATALOG:-$ROOT/infrastructure/aws/secrets-catalog.json}"

if [[ -z "$REGION" ]]; then
  echo "Set AWS_REGION" >&2
  exit 1
fi
if [[ ! -f "$CATALOG" ]]; then
  echo "Catalog not found: $CATALOG" >&2
  exit 1
fi
if ! command -v aws >/dev/null || ! command -v jq >/dev/null; then
  echo "aws and jq are required" >&2
  exit 1
fi

mkdir -p "$SECRETS_DIR"
umask 077

mapfile -t NAMES < <(jq -r '.secrets[].name' "$CATALOG")
OK=0
SKIP=0

for name in "${NAMES[@]}"; do
  secret_id="speroflow/${ENV_NAME}/${name}"
  dest="${SECRETS_DIR}/${name}"
  optional="$(jq -r --arg n "$name" '.secrets[] | select(.name==$n) | (.optional // false)' "$CATALOG")"
  kind="$(jq -r --arg n "$name" '.secrets[] | select(.name==$n) | .kind' "$CATALOG")"
  encoding="$(jq -r --arg n "$name" '.secrets[] | select(.name==$n) | (.encoding // empty)' "$CATALOG")"
  if [[ -z "$encoding" || "$encoding" == "null" ]]; then
    if [[ "$kind" == "pfx" ]]; then
      encoding="binary"
    else
      encoding="text"
    fi
  fi

  if ! json="$(aws secretsmanager get-secret-value --secret-id "$secret_id" --region "$REGION" --output json 2>/dev/null)"; then
    if [[ "$optional" == "true" ]]; then
      echo "SKIP optional missing: $name"
      SKIP=$((SKIP + 1))
      continue
    fi
    echo "ERROR required secret missing: $secret_id" >&2
    exit 1
  fi

  has_binary="$(echo "$json" | jq -r 'if .SecretBinary then "yes" else "no" end')"
  if [[ "$encoding" == "binary" || "$has_binary" == "yes" ]]; then
    b64="$(echo "$json" | jq -r '.SecretBinary // .SecretString')"
    echo "$b64" | base64 -d >"$dest"
  else
    # Write SecretString without a trailing CR; ensure single trailing newline.
    echo "$json" | jq -r '.SecretString' | tr -d '\r' | sed -e '$a\' >"$dest"
  fi
  chmod 600 "$dest"
  echo "OK $name"
  OK=$((OK + 1))
done

echo "Pull complete written=$OK skipped=$SKIP dir=$SECRETS_DIR"
