#!/usr/bin/env sh
# Publish docs/wiki/ to the GitHub Wiki remote.
#
# Clones the wiki repository into a temporary directory, mirrors docs/wiki/
# into that clone, commits with a timestamp and current HEAD SHA, then pushes.
# The source docs/wiki/ directory is never modified.
#
# Usage:
#   ./scripts/publish-wiki.sh
#   ./scripts/publish-wiki.sh --dry-run

set -eu

DRY_RUN=0
while [ "$#" -gt 0 ]; do
  case "$1" in
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    -h|--help)
      sed -n '2,10p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

WIKI_REMOTE="https://github.com/daonhan/Microservices-in-.NET.wiki.git"
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
WIKI_SOURCE="$REPO_ROOT/docs/wiki"

run_git() {
  workdir=$1
  shift
  (cd "$workdir" && git "$@")
}

command -v git >/dev/null 2>&1 || {
  echo "git CLI is required." >&2
  exit 1
}

if [ ! -d "$WIKI_SOURCE" ]; then
  echo "Wiki source directory not found: $WIKI_SOURCE" >&2
  exit 1
fi

HEAD_SHA=$(run_git "$REPO_ROOT" rev-parse --short HEAD)
TIMESTAMP=$(date -u +"%Y%m%d-%H%M%S")
MESSAGE="Sync wiki from docs/wiki $TIMESTAMP (repo $HEAD_SHA)"

if [ "$DRY_RUN" -eq 1 ]; then
  echo "==> Dry run: wiki publish plan"
  echo "    repo root: $REPO_ROOT"
  echo "    source:    $WIKI_SOURCE"
  echo "    remote:    $WIKI_REMOTE"
  echo "    commit:    $MESSAGE"
  echo "    would clone wiki remote into a temporary directory"
  echo "    would replace clone contents with docs/wiki/ files"
  echo "    would commit if the clone has changes"
  echo "    would push to the wiki remote"
  exit 0
fi

TMP_PARENT=${TMPDIR:-/tmp}
TMP_ROOT=$(mktemp -d "$TMP_PARENT/nhamnhi-wiki-publish.XXXXXX")
CLONE_DIR="$TMP_ROOT/wiki"

cleanup() {
  if [ -d "$TMP_ROOT" ]; then
    rm -rf "$TMP_ROOT"
  fi
}
trap cleanup EXIT HUP INT TERM

echo "==> 1/4 Cloning wiki remote"
run_git "$REPO_ROOT" clone --depth 1 "$WIKI_REMOTE" "$CLONE_DIR"

case "$CLONE_DIR" in
  "$TMP_ROOT"/*) ;;
  *)
    echo "Refusing to mirror outside temp root: $CLONE_DIR" >&2
    exit 1
    ;;
esac

echo "==> 2/4 Mirroring docs/wiki/ into temp clone"
for entry in "$CLONE_DIR"/* "$CLONE_DIR"/.[!.]* "$CLONE_DIR"/..?*; do
  [ -e "$entry" ] || continue
  [ "$(basename -- "$entry")" = ".git" ] && continue
  rm -rf "$entry"
done
cp -R "$WIKI_SOURCE"/. "$CLONE_DIR"/

echo "==> 3/4 Committing wiki changes"
run_git "$CLONE_DIR" add -A
WIKI_STATUS=$(run_git "$CLONE_DIR" status --porcelain)
if [ -z "$WIKI_STATUS" ]; then
  echo "    no wiki changes to publish"
  exit 0
fi
run_git "$CLONE_DIR" commit -m "$MESSAGE"

echo "==> 4/4 Pushing wiki changes"
run_git "$CLONE_DIR" push
echo "Wiki publish complete."
