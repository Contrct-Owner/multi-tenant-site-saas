#!/bin/bash
# Stop hook: run the (fast) architecture test project if it exists.
# Exit 2 feeds stderr back to Claude so boundary violations surface immediately.
ARCH_PROJ=$(find "${CLAUDE_PROJECT_DIR:-.}" -maxdepth 4 -name "*.ArchitectureTests.csproj" 2>/dev/null | head -1)
[ -z "$ARCH_PROJ" ] && exit 0   # no solution yet
OUT=$(dotnet test "$ARCH_PROJ" --nologo -v q 2>&1)
if [ $? -ne 0 ]; then
  echo "Architecture tests failed - module boundary or convention violation:" >&2
  echo "$OUT" | tail -30 >&2
  exit 2
fi
exit 0
