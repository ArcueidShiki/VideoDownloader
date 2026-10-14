#!/bin/sh
# Explicit opt-in workflow; never installed as an automatic hook.
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
if [ "$#" -lt 1 ]; then
  echo 'Usage: scripts/push.sh /absolute/archive-directory [git push arguments...]' >&2
  exit 2
fi
ARCHIVE=$1
shift
python3 "$ROOT/scripts/repo_hygiene.py" check
python3 "$ROOT/scripts/repo_hygiene.py" clean --archive-dir "$ARCHIVE" --apply
git -C "$ROOT" push "$@"
