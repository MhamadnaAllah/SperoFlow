#!/bin/sh
set -eu

if [ -f /run/secrets/knowledge_redis_password ]; then
  export KNOWLEDGE_REDIS_URL="redis://:$(cat /run/secrets/knowledge_redis_password)@knowledge-redis:6379/0"
fi

if [ -f /run/secrets/knowledge_neo4j_writer_password ]; then
  export KNOWLEDGE_NEO4J_PASSWORD="$(cat /run/secrets/knowledge_neo4j_writer_password)"
fi

if [ -f /run/secrets/knowledge_minio_access_key ]; then
  export KNOWLEDGE_OBJECT_STORAGE_ACCESS_KEY="$(cat /run/secrets/knowledge_minio_access_key)"
fi

if [ -f /run/secrets/knowledge_minio_secret_key ]; then
  export KNOWLEDGE_OBJECT_STORAGE_SECRET_KEY="$(cat /run/secrets/knowledge_minio_secret_key)"
fi

exec "$@"
