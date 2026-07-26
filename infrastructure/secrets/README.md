# Docker Secrets

This directory is ignored by Git. Generate secrets on a trusted deployment host:

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap-secrets.ps1
~~~

The script preserves existing material by default. Use -Rotate only in a
planned rotation with a validated rollback.

## Main Application

- postgres_password, redis_password, neo4j_password
- minio_access_key, minio_secret_key
- admin_bootstrap_token
- service_jwt_private_key, service_jwt_public_key
- oidc_signing_certificate, oidc_signing_certificate_password
- oidc_encryption_certificate, oidc_encryption_certificate_password
- smtp_password only when SMTP is configured

## Knowledge Platform

- knowledge_postgres_password, knowledge_redis_password
- knowledge_minio_access_key, knowledge_minio_secret_key
- knowledge_neo4j_password
- knowledge_neo4j_writer_password for knowledge-worker only
- knowledge_neo4j_reader_password for main ai-api only
- knowledge_service_jwt_private_key, knowledge_service_jwt_public_key
- knowledge_grant_private_key, knowledge_grant_public_key
- knowledge_portal_data_protection_certificate
- knowledge_portal_data_protection_certificate_password

The main API receives its own service private key to request grants. It never
receives knowledge PostgreSQL, Redis, MinIO, graph-writer, grant-signing, or
portal data-protection secrets.
