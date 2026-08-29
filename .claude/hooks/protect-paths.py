#!/usr/bin/env python3
"""PreToolUse guard for Edit/Write.

- Blocks edits to already-applied EF migrations (files under a Migrations/
  directory that already exist on disk). Add a new migration instead.
- Asks for confirmation on edits under docs/decisions/ (settled ADRs).
"""
import json, os, sys

data = json.load(sys.stdin)
fp = (data.get("tool_input") or {}).get("file_path") or ""
if not fp:
    sys.exit(0)
rel = os.path.relpath(fp, os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))
parts = rel.split(os.sep)

def respond(decision, reason):
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": decision,
        "permissionDecisionReason": reason}}))
    sys.exit(0)

# Applied migrations are immutable (ADR 17: each module owns its migration history)
if "Migrations" in parts and rel.endswith(".cs") and os.path.exists(fp):
    respond("deny",
        "Applied migrations are immutable. Add a NEW migration (use the "
        "new-migration skill) instead of editing an existing one.")

# Settled decisions need deliberate change
if rel.startswith("docs/decisions/") and os.path.exists(fp):
    respond("ask",
        "This is a settled architecture decision (see docs/decisions/README.md). "
        "Confirm you intend to change or supersede it.")

sys.exit(0)
