-- SperoFlow AI — Legacy Knowledge Table Retirement Migration
-- Drops legacy main-stack knowledge tables after cutover to Knowledge Platform service.
-- ONLY run after verifying Knowledge Platform dataset ingestion and access grant workflows.

BEGIN;

DROP TABLE IF EXISTS app.dataset_ingestion_jobs CASCADE;
DROP TABLE IF EXISTS app.knowledge_source_files CASCADE;
DROP TABLE IF EXISTS app.knowledge_datasets CASCADE;

COMMIT;
