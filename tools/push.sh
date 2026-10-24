#!/usr/bin/env bash
# Push to origin and immediately follow the CI run it triggers.
#
#   tools/push.sh              # push main, then watch
#   tools/push.sh --no-watch   # push only
#   tools/push.sh --tag v0.2.0 # push, then tag (which auto-releases) and watch both
#
# A plain `git push` already triggers ci on GitHub — this wrapper only adds live feedback,
# so nothing here is required for the pipeline to run.
set -euo pipefail

REPO="rreon633-design/RADHA-SHELL"
BRANCH="${BRANCH:-main}"
WATCH=1
TAG=""

while [ $# -gt 0 ]; do
  case "$1" in
    --no-watch) WATCH=0 ;;
    --tag) TAG="${2:-}"; shift ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

cd "$(dirname "$0")/.."

echo "→ pushing $BRANCH"
git push origin "$BRANCH"

if [ -n "$TAG" ]; then
  echo "→ tagging $TAG (this triggers the release workflow)"
  git tag -a "$TAG" -m "RADHA SHELL $TAG" 2>/dev/null || true
  git push origin "$TAG"
fi

[ "$WATCH" -eq 1 ] || exit 0

echo "→ waiting for the workflow GitHub just queued…"
sleep 8

api() { curl -sf -m 20 "https://api.github.com/repos/$REPO/$1"; }

run_id=""
for _ in $(seq 1 20); do
  run_id=$(api "actions/runs?per_page=1" \
    | python3 -c 'import json,sys; r=json.load(sys.stdin)["workflow_runs"]; print(r[0]["id"] if r else "")' 2>/dev/null || true)
  [ -n "$run_id" ] && break
  sleep 5
done
[ -n "$run_id" ] || { echo "no run appeared (is Actions enabled?)"; exit 1; }

echo "   run $run_id — https://github.com/$REPO/actions/runs/$run_id"

prev=""
while :; do
  out=$(api "actions/runs/$run_id/jobs?per_page=20" \
    | python3 -c '
import json,sys
d=json.load(sys.stdin)
for j in d.get("jobs",[]):
    st=j["conclusion"] or j["status"]
    mark={"success":"PASS","failure":"FAIL","skipped":"skip","cancelled":"stop"}.get(st,st[:4])
    print(f"{mark:5} {j[\"name\"]}")
' 2>/dev/null || true)
  status=$(api "actions/runs/$run_id" \
    | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["status"], d["conclusion"] or "-")' 2>/dev/null || echo "in_progress -")

  if [ "$out" != "$prev" ] && [ -n "$out" ]; then printf '%s\n' "$out"; prev="$out"; fi

  set -- $status
  if [ "${1:-}" = "completed" ]; then
    echo
    [ "${2:-}" = "success" ] && echo "✓ CI green — a rolling v0.0.0-dev release was published automatically" \
                             || echo "✗ CI ${2:-failed} — open the run link above"
    [ "${2:-}" = "success" ] || exit 1
    break
  fi
  sleep 15
done
