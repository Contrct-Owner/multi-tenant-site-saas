#!/usr/bin/env python3
"""PostToolUse: format the file that was just edited/written.
C# -> CSharpier, TS/JS/JSON/CSS -> oxfmt.
No-ops gracefully while tooling/projects don't exist yet."""
import json, os, shutil, subprocess, sys

data = json.load(sys.stdin)
fp = (data.get("tool_input") or {}).get("file_path") or ""
if not fp:
    sys.exit(0)
root = os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd())

def run(cmd):
    try:
        subprocess.run(cmd, capture_output=True, timeout=60, cwd=root)
    except Exception:
        pass

if fp.endswith(".cs"):
    # local dotnet tool (dotnet-tools.json manifest) first, then global install
    if os.path.exists(os.path.join(root, ".config", "dotnet-tools.json")) and shutil.which("dotnet"):
        run(["dotnet", "csharpier", "format", fp])
    elif shutil.which("csharpier"):
        run(["csharpier", "format", fp])
elif fp.endswith((".ts", ".tsx", ".js", ".jsx", ".json", ".css")):
    if shutil.which("oxfmt"):
        run(["oxfmt", fp])
    elif shutil.which("pnpm"):
        run(["pnpm", "exec", "oxfmt", fp])
sys.exit(0)
