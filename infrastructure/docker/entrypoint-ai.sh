#!/bin/sh
set -e

if [ -f /run/secrets/neo4j_password ]; then
  export NEO4J_PASSWORD="$(cat /run/secrets/neo4j_password)"
fi
if [ -f /run/secrets/knowledge_neo4j_reader_password ]; then
  export KNOWLEDGE_NEO4J_PASSWORD="$(cat /run/secrets/knowledge_neo4j_reader_password)"
fi
if [ -f /run/secrets/redis_password ]; then
  REDIS_PASSWORD="$(cat /run/secrets/redis_password)"
  export REDIS_URL="redis://:$REDIS_PASSWORD@redis:6379/0"
fi
if [ -f /run/secrets/minio_access_key ]; then
  export OBJECT_STORAGE_ACCESS_KEY="$(cat /run/secrets/minio_access_key)"
fi
if [ -f /run/secrets/minio_secret_key ]; then
  export OBJECT_STORAGE_SECRET_KEY="$(cat /run/secrets/minio_secret_key)"
fi
if [ -f /run/secrets/service_jwt_public_key ]; then
  export SERVICE_JWT_PUBLIC_KEY_PATH="/run/secrets/service_jwt_public_key"
fi
if [ -f /run/secrets/knowledge_grant_public_key ]; then
  export KNOWLEDGE_GRANT_PUBLIC_KEY_PATH="/run/secrets/knowledge_grant_public_key"
fi

exec "$@"
