"""
MarkdownParser — Parses markdown content files for topic enrichment.

Node IDs are embedded in the filename after an '@' character.
"""

from __future__ import annotations

import logging
import re
from pathlib import Path

from speroflow_ai.models.graph import ContentEntry

logger = logging.getLogger(__name__)

NODE_ID_PATTERN = re.compile(r"@([^@]+)$")
H1_PATTERN = re.compile(r"^#\s+(.+)", re.MULTILINE)


class MarkdownParser:
    """
    Parses markdown files to extract content for topic nodes.
    """

    def parse_content_dir(self, content_path: Path) -> dict[str, str]:
        """
        Parse all .md files in a directory into a {node_id: content} map.
        """
        content_map: dict[str, str] = {}
        if not content_path.exists() or not content_path.is_dir():
            logger.warning("Content directory not found: %s", content_path)
            return content_map

        md_files = list(content_path.glob("*.md"))
        parsed = skipped = 0

        for md_file in md_files:
            node_id = self._extract_node_id(md_file.stem)
            if not node_id:
                skipped += 1
                continue
            try:
                text = md_file.read_text(encoding="utf-8").strip()
                content_map[node_id] = text
                parsed += 1
            except (OSError, UnicodeDecodeError) as exc:
                logger.error("Failed to read %s: %s", md_file, exc)
                skipped += 1

        logger.info(
            "Parsed %d markdown files, skipped %d from %s",
            parsed, skipped, content_path,
        )
        return content_map

    def parse_content_as_nodes(self, content_path: Path) -> list[ContentEntry]:
        """
        Create ContentEntry objects from markdown files (content-only roadmaps).
        """
        entries: list[ContentEntry] = []
        if not content_path.exists() or not content_path.is_dir():
            return entries

        for md_file in content_path.glob("*.md"):
            node_id = self._extract_node_id(md_file.stem)
            if not node_id:
                continue
            try:
                text = md_file.read_text(encoding="utf-8").strip()
            except (OSError, UnicodeDecodeError) as exc:
                logger.error("Failed to read %s: %s", md_file, exc)
                continue
            label = self._extract_label(md_file.stem, text)
            entries.append(ContentEntry(node_id=node_id, label=label, content=text))

        return entries

    @staticmethod
    def _extract_node_id(filename_stem: str) -> str | None:
        match = NODE_ID_PATTERN.search(filename_stem)
        return match.group(1) if match else None

    @staticmethod
    def _extract_label(filename_stem: str, content: str) -> str:
        h1 = H1_PATTERN.search(content)
        if h1:
            return h1.group(1).strip()
        slug_part = filename_stem.split("@")[0] if "@" in filename_stem else filename_stem
        if slug_part:
            label = slug_part.replace("--", " - ").replace("-", " ").strip()
            return label.title() if label else "Untitled"
        return "Untitled"
