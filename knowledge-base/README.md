# SperoFlow Knowledge Base

This directory contains version-controlled curated learning roadmaps and CBT
source material. It is an input to graph construction, not a runtime data
store. Do not modify its contents from an API, worker, or application startup
hook.

## Layout

- `roadmaps/`: curated roadmap source files.
- `cbt/source/`: original CBT Markdown source material.
- `cbt/graph/`: checked-in parser and graph artifacts derived from the source.
- `manifest.json`: SHA-256 integrity manifest for all curated assets.

## Integrity Verification

Run this before a release, after copying the repository, and before a graph
rebuild:

```powershell
python ai-worker/scripts/knowledge_manifest.py verify `
  --root knowledge-base `
  --layout canonical `
  --manifest knowledge-base/manifest.json
```

The command must report the expected file count and hashes. A mismatch stops a
release investigation; do not silently regenerate the manifest.

## Publication

The main application does not ingest this directory. After verifying the
manifest, a KnowledgeOwner uploads the original roadmap and CBT source files to
the private Knowledge Portal, then a KnowledgeAdmin reviews and publishes the
validated release. Do not upload the derived `cbt/graph` artifacts and do not
use the retired main-stack knowledge bootstrap command.

The isolated knowledge worker is the only graph writer. It creates a complete,
versioned release before any reader can receive a grant for the new graph data.