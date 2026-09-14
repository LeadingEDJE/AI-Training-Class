#!/usr/bin/env python3
"""Normalize an emitted OpenAPI document into the committed-snapshot form (#141).

Usage: normalize-openapi.py <input.json> <output.json>

Two things make the raw GetDocument output non-deterministic across platforms, and both are removed
here so the snapshot generated on a Windows dev box is byte-identical to the one CI generates on
Linux — otherwise the freshness check trips on line endings alone:

  1. File line endings — we always write LF (newline='\n').
  2. Embedded CR *inside string values* — the `description` fields carry multi-line C# doc strings,
     whose newlines are baked into the JSON as `\r\n` when the .cs source is checked out CRLF and
     `\n` when it is LF. We collapse `\r\n` and any lone `\r` to `\n` in every string value.

Formatting is a stable 2-space indent; keys keep GetDocument's deterministic emission order.
"""
import json
import sys


def strip_cr(node):
    if isinstance(node, str):
        return node.replace("\r\n", "\n").replace("\r", "\n")
    if isinstance(node, list):
        return [strip_cr(item) for item in node]
    if isinstance(node, dict):
        return {key: strip_cr(value) for key, value in node.items()}
    return node


def main() -> None:
    src, dst = sys.argv[1], sys.argv[2]
    with open(src, encoding="utf-8") as handle:
        data = strip_cr(json.load(handle))
    with open(dst, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(data, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


if __name__ == "__main__":
    main()
