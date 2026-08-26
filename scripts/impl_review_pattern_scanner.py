#!/usr/bin/env python3
"""Scan context/archive/*/reviews/impl-review.md for recurring finding
patterns not yet captured in context/foundation/lessons.md.

Usage: python scripts/impl_review_pattern_scanner.py [--root PATH] [--threshold 0.2]
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import defaultdict
from pathlib import Path

FINDING_HEADER_RE = re.compile(r"^(F\d+)\s*—\s*(.+)$")
FIELD_RE = {
    "dimension": re.compile(r"\*\*Dimension\*\*:\s*(.+)"),
    "detail": re.compile(r"\*\*Detail\*\*:\s*(.+)"),
}

STOPWORDS = {
    # generic English
    "the", "a", "an", "and", "or", "but", "is", "are", "was", "were", "be",
    "been", "to", "of", "in", "on", "at", "for", "with", "by", "as", "it",
    "this", "that", "these", "those", "not", "no", "than", "then", "so",
    "if", "when", "while", "into", "onto", "from", "its", "their", "which",
    "would", "could", "should", "does", "doesn", "isn", "still", "already",
    "instead", "before", "after", "each", "one", "two", "three", "per",
    "via", "own", "any", "all", "same", "here", "there", "only", "just",
    "more", "most", "over", "under", "without", "within", "across",
    # domain filler that shows up in nearly every finding
    "license", "licenses", "panel", "test", "tests", "build", "verified",
    "plan", "phase", "dotnet", "razor", "code", "file", "files", "method",
    "class", "function", "line", "lines", "review", "dimension", "decision",
    "fixed", "skip", "skipped", "dismissed", "accepted", "none", "using",
    "use", "used", "new", "add", "added", "change", "changes", "changed",
}

TOKEN_RE = re.compile(r"[a-zA-Z][a-zA-Z\-]{2,}")


class Finding:
    def __init__(self, change: str, fid: str, title: str, dimension: str, detail: str):
        self.change = change
        self.fid = fid
        self.title = title
        self.dimension = dimension or "(unspecified)"
        self.detail = detail
        self.tokens = tokenize(f"{title} {detail}")

    def label(self) -> str:
        return f"{self.change} {self.fid}: {self.title}"


def tokenize(text: str) -> set[str]:
    words = (w.lower() for w in TOKEN_RE.findall(text))
    return {w for w in words if w not in STOPWORDS}


def parse_review_file(path: Path) -> list[Finding]:
    change = path.parents[1].name  # .../reviews/impl-review.md -> parents[1] = change folder
    text = path.read_text(encoding="utf-8")
    chunks = text.split("\n### ")[1:]  # drop preamble before first "### F..." heading
    findings = []
    for chunk in chunks:
        chunk = chunk.split("\n## ", 1)[0]  # cut off before next level-2 section
        lines = chunk.splitlines()
        if not lines:
            continue
        header = FINDING_HEADER_RE.match(lines[0].strip())
        if not header:
            continue
        fid, title = header.group(1), header.group(2).strip()
        fields = {"dimension": "", "detail": ""}
        for line in lines[1:]:
            for key, pattern in FIELD_RE.items():
                if key in fields:
                    m = pattern.search(line)
                    if m:
                        fields[key] = m.group(1).strip()
        findings.append(Finding(change, fid, title, fields["dimension"], fields["detail"]))
    return findings


def jaccard(a: set[str], b: set[str]) -> float:
    if not a or not b:
        return 0.0
    return len(a & b) / len(a | b)


def cluster_findings(findings: list[Finding], threshold: float) -> list[list[Finding]]:
    parent = list(range(len(findings)))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(i: int, j: int) -> None:
        ri, rj = find(i), find(j)
        if ri != rj:
            parent[ri] = rj

    for i in range(len(findings)):
        for j in range(i + 1, len(findings)):
            if jaccard(findings[i].tokens, findings[j].tokens) >= threshold:
                union(i, j)

    groups: dict[int, list[Finding]] = defaultdict(list)
    for idx, f in enumerate(findings):
        groups[find(idx)].append(f)

    clusters = [g for g in groups.values() if len(g) >= 2]
    clusters.sort(key=len, reverse=True)
    return clusters


def top_tokens(findings: list[Finding], n: int = 3) -> list[str]:
    counts: dict[str, int] = defaultdict(int)
    for f in findings:
        for tok in f.tokens:
            counts[tok] += 1
    return [tok for tok, _ in sorted(counts.items(), key=lambda kv: kv[1], reverse=True)[:n]]


def already_in_lessons(cluster_tokens: list[str], lessons_text: str) -> bool:
    lessons_lower = lessons_text.lower()
    hits = sum(1 for tok in cluster_tokens if tok in lessons_lower)
    return hits >= max(1, len(cluster_tokens) // 2 + 1)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--threshold", type=float, default=0.2, help="Jaccard similarity threshold for clustering")
    args = parser.parse_args()

    sys.stdout.reconfigure(encoding="utf-8")  # Windows console default codepage can't render em dashes

    review_files = sorted(args.root.glob("context/archive/*/reviews/impl-review.md"))
    if not review_files:
        print(f"No impl-review.md files found under {args.root}/context/archive/*/reviews/")
        return

    findings: list[Finding] = []
    for path in review_files:
        findings.extend(parse_review_file(path))

    print(f"=== Impl-review pattern scanner ===")
    print(f"Scanned {len(review_files)} files, found {len(findings)} findings.\n")

    print("--- Grouped by Dimension ---")
    by_dimension: dict[str, list[Finding]] = defaultdict(list)
    for f in findings:
        by_dimension[f.dimension].append(f)
    for dim, group in sorted(by_dimension.items(), key=lambda kv: len(kv[1]), reverse=True):
        changes = sorted({f.change for f in group})
        print(f"{dim:<24}: {len(group)} finding(s)  (changes: {', '.join(changes)})")

    lessons_path = args.root / "context" / "foundation" / "lessons.md"
    lessons_text = lessons_path.read_text(encoding="utf-8") if lessons_path.exists() else ""

    print("\n--- Recurring themes (keyword-overlap heuristic) ---")
    clusters = cluster_findings(findings, args.threshold)
    if not clusters:
        print("No recurring themes found above the similarity threshold.")
    for cluster in clusters:
        keywords = top_tokens(cluster)
        tag = "[already in lessons.md]" if already_in_lessons(keywords, lessons_text) else "[/10x-lesson candidate]"
        print(f"\n{tag} keywords: {', '.join(keywords)} — {len(cluster)} occurrence(s)")
        for f in cluster:
            print(f"  - {f.label()}")


if __name__ == "__main__":
    main()
