#!/bin/sh
set -e

POSTGRES_PASSWORD="$(cat /run/secrets/postgres_password)"
REDIS_PASSWORD="$(cat /run/secrets/redis_password)"

export ConnectionStrings__Postgres="Host=postgres;Port=5432;Database=speroflow;Username=speroflow_app;Password=$POSTGRES_PASSWORD"
export Redis__ConnectionString="redis:6379,password=$REDIS_PASSWORD,abortConnect=false"

case "${ObjectStorage__Provider:-Minio}" in
  S3|s3|AWS|aws)
    # The AWS SDK discovers ECS task-role credentials through its default chain.
    # Do not mount or export long-lived S3 access keys here.
    ;;
  *)
    MINIO_ACCESS_KEY="$(cat /run/secrets/minio_access_key)"
    MINIO_SECRET_KEY="$(cat /run/secrets/minio_secret_key)"
    export ObjectStorage__AccessKey="$MINIO_ACCESS_KEY"
    export ObjectStorage__SecretKey="$MINIO_SECRET_KEY"
    ;;
esac

if [ -f /run/secrets/smtp_password ]; then
  export Smtp__Password="$(cat /run/secrets/smtp_password)"
fi

exec "$@"
