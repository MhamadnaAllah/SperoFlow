#!/bin/sh
set -eu

if [ -f /run/secrets/knowledge_postgres_password ]; then
  export KnowledgeDatabase__ConnectionString="Host=${KNOWLEDGE_POSTGRES_HOST:-knowledge-postgres};Port=5432;Database=speroflow_knowledge;Username=speroflow_knowledge;Password=$(cat /run/secrets/knowledge_postgres_password)"
fi

if [ -f /run/secrets/knowledge_redis_password ]; then
  export KnowledgeRedis__ConnectionString="knowledge-redis:6379,password=$(cat /run/secrets/knowledge_redis_password),abortConnect=false"
fi

if [ -f /run/secrets/knowledge_minio_access_key ]; then
  export KnowledgeStorage__AccessKey="$(cat /run/secrets/knowledge_minio_access_key)"
fi

if [ -f /run/secrets/knowledge_minio_secret_key ]; then
  export KnowledgeStorage__SecretKey="$(cat /run/secrets/knowledge_minio_secret_key)"
fi

exec "$@"
