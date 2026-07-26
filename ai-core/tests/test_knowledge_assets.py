from __future__ import annotations

import unittest
from pathlib import Path

from speroflow_ai.parsers.cbt_parser import CBTParser


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class KnowledgeAssetTests(unittest.TestCase):
    def test_cbt_parser_reads_the_versioned_knowledge_base(self) -> None:
        graph = CBTParser(
            data_dir=REPOSITORY_ROOT / "knowledge-base" / "cbt" / "graph",
            source_root=REPOSITORY_ROOT / "knowledge-base" / "cbt" / "source",
        ).parse()
        self.assertEqual(len(graph.domains), 18)
        self.assertEqual(len(graph.documents), 320)
        self.assertGreater(len(graph.sections), len(graph.documents))
        self.assertEqual(len({document.node_id for document in graph.documents}), len(graph.documents))


if __name__ == "__main__":
    unittest.main()