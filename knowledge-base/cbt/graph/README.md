# CBT Graph Data

This directory contains provenance metadata and clinician-review data for the source markdown in `../CBT-Data-md/`. `parsed-documents.jsonl` records deterministic section anchors and hashes, but intentionally does not duplicate the source prose.

The source material is attributed to the Centre for Clinical Interventions (CCI). CCI's copyright and disclaimer terms must be reviewed and appropriate permission confirmed before the source content is indexed for a deployed or user-facing service.

`manifest.json` and `parsed-documents.jsonl` are rebuilt with `python -m scripts.build_cbt_graph_data`. A rebuild preserves existing taxonomy files. Only taxonomy records with `review_status` set to `approved`, reviewer identity/date, source document IDs, and evidence locators are eligible for graph ingestion.
