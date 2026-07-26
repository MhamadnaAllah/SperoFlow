# Local Dataset Ingestion (MinIO)

Use the MinIO overlay only for local development or a self-contained demo. It keeps the
base stack private while exposing port 9000 so the browser can PUT directly to a short-lived,
checksum-bound URL.

## Start the stack

Create the normal secrets under `infrastructure/secrets/`, including `minio_access_key` and
`minio_secret_key`, then run:

```powershell
docker compose -f compose.yaml -f compose.minio-dataset-ingestion.yaml up --build
```

On Docker Desktop, the default signed browser endpoint is `host.docker.internal:9000`. On a
Linux Docker host, set `MINIO_PUBLIC_ENDPOINT=localhost:9000` before starting Compose. Set
`MINIO_CORS_ORIGIN` to the actual browser origin (for example `https://app.local.test`) if it
is not `http://localhost:3000`.

## What the overlay configures

- API presigned URLs use the host-visible MinIO endpoint and HTTP only for local development.
- `ai-worker` receives the private Docker-network endpoint and MinIO credentials via Docker
  secrets, so it can download files after upload.
- MinIO allows PUT preflight requests only from the configured local web origin. For a
  production deployment use the S3 setup in `AWS_DATASET_INGESTION.md`, not this overlay.

The frontend sends uploads with `credentials: omit`; do not make the bucket public or put
Neo4j, AWS, or worker credentials in the browser.
