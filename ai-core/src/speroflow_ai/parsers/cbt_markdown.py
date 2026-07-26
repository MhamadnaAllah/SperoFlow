"""Deterministic Markdown section parsing for CBT source documents."""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass
from typing import Any


HEADING_PATTERN = re.compile(r"^(#{1,6})\s+(.+?)\s*$")


@dataclass(frozen=True)
class ParsedCBTSection:
    """A source-grounded section with stable line anchors."""

    section_id: str
    document_id: str
    domain_id: str
    title: str
    heading_level: int
    ordinal: int
    source_relpath: str
    start_line: int
    end_line: int
    content: str
    parent_section_id: str = ""

    @property
    def content_sha256(self) -> str:
        return hashlib.sha256(self.content.encode("utf-8")).hexdigest()

    @property
    def line_count(self) -> int:
        return max(0, self.end_line - self.start_line + 1)

    @property
    def source_anchor(self) -> str:
        return f"{self.source_relpath}#L{self.start_line}-L{self.end_line}"

    def to_metadata_dict(self) -> dict[str, Any]:
        """Return parse metadata without duplicating the source prose."""
        return {
            "section_id": self.section_id,
            "document_id": self.document_id,
            "domain_id": self.domain_id,
            "title": self.title,
            "heading_level": self.heading_level,
            "ordinal": self.ordinal,
            "parent_section_id": self.parent_section_id,
            "source_relpath": self.source_relpath,
            "source_anchor": self.source_anchor,
            "start_line": self.start_line,
            "end_line": self.end_line,
            "line_count": self.line_count,
            "char_count": len(self.content),
            "content_sha256": self.content_sha256,
        }


def slugify(value: str) -> str:
    normalized = value.lower().replace("&", " and ")
    normalized = re.sub(r"[^a-z0-9]+", "-", normalized)
    return normalized.strip("-") or "untitled"


def clean_heading(value: str) -> str:
    """Normalize visible Markdown heading text for metadata only."""
    cleaned = re.sub(r"!\[([^\]]*)\]\([^)]+\)", r"\1", value)
    cleaned = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", cleaned)
    cleaned = re.sub(r"<[^>]+>", "", cleaned)
    cleaned = re.sub(r"[*_~`#]+", "", cleaned)
    cleaned = re.sub(r"\s+", " ", cleaned).strip(" -:")
    return cleaned or "Untitled section"


def parse_markdown_sections(
    *,
    document_id: str,
    domain_id: str,
    source_relpath: str,
    text: str,
) -> list[ParsedCBTSection]:
    """Split a Markdown document into heading-bounded source sections.

    The parser does not infer CBT concepts. It only records deterministic
    source anchors so downstream steps can cite and review exact passages.
    """
    lines = text.splitlines()
    if not lines:
        lines = [""]

    headings: list[tuple[int, int, str]] = []
    for index, line in enumerate(lines, start=1):
        match = HEADING_PATTERN.match(line.strip())
        if match:
            headings.append((index, len(match.group(1)), clean_heading(match.group(2))))

    raw_sections: list[dict[str, Any]] = []
    if headings:
        first_heading_line = headings[0][0]
        preface_lines = lines[: first_heading_line - 1]
        if any(line.strip() for line in preface_lines):
            raw_sections.append(
                {
                    "title": "Preface",
                    "heading_level": 0,
                    "start_line": 1,
                    "end_line": first_heading_line - 1,
                }
            )
        for index, (line_number, level, title) in enumerate(headings):
            next_line = headings[index + 1][0] if index + 1 < len(headings) else len(lines) + 1
            raw_sections.append(
                {
                    "title": title,
                    "heading_level": level,
                    "start_line": line_number,
                    "end_line": max(line_number, next_line - 1),
                }
            )
    else:
        raw_sections.append(
            {
                "title": "Document body",
                "heading_level": 0,
                "start_line": 1,
                "end_line": len(lines),
            }
        )

    sections: list[ParsedCBTSection] = []
    stack: list[tuple[int, str]] = []
    for ordinal, raw in enumerate(raw_sections, start=1):
        level = int(raw["heading_level"])
        title = str(raw["title"])
        while stack and stack[-1][0] >= level:
            stack.pop()
        parent_section_id = stack[-1][1] if stack and level > 0 else ""
        start_line = int(raw["start_line"])
        end_line = int(raw["end_line"])
        content = "\n".join(lines[start_line - 1 : end_line])
        section_id = f"cbt-sec-{document_id.removeprefix('cbt-doc-')}-{ordinal:04d}-{slugify(title)}"
        section = ParsedCBTSection(
            section_id=section_id,
            document_id=document_id,
            domain_id=domain_id,
            title=title,
            heading_level=level,
            ordinal=ordinal,
            source_relpath=source_relpath,
            start_line=start_line,
            end_line=end_line,
            content=content,
            parent_section_id=parent_section_id,
        )
        sections.append(section)
        if level > 0:
            stack.append((level, section.section_id))

    return sections
