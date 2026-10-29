#!/usr/bin/env bash
#
# Publish docs/wiki/ to the repository's GitHub wiki.
#
#   scripts/publish-wiki.sh              # clone/update the wiki repo and push
#   scripts/publish-wiki.sh --dry-run    # show what would change, push nothing
#   scripts/publish-wiki.sh --message "…" # custom commit subject
#
# Environment:
#   DOOPASS_WIKI_URL      override the wiki remote (default: origin's repo + .wiki.git)
#   DOOPASS_WIKI_WORKDIR  where the wiki clone lives (default: <repo>/.wiki-work)
#
# Notes:
#   * GitHub wikis are a separate git repository. It does not exist until the
#     first page has been created in the web UI (Repository → Wiki → Create the
#     first page). Until then the clone fails with "Repository not found".
#   * Authentication is whatever your git credential helper / gh provides.
#     `gh auth login` followed by `gh auth setup-git` is enough for HTTPS.
#   * The wiki repository only contains Markdown pages; _Sidebar.md and
#     _Footer.md are special pages GitHub renders around every page.
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
SOURCE_DIR="$REPO_ROOT/docs/wiki"
WORK_DIR="${DOOPASS_WIKI_WORKDIR:-$REPO_ROOT/.wiki-work}"

DRY_RUN=0
MESSAGE=""

while [ $# -gt 0 ]; do
    case "$1" in
        --dry-run)
            DRY_RUN=1
            shift
            ;;
        --message)
            MESSAGE="${2:-}"
            shift 2
            ;;
        --help|-h)
            sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "error: unknown argument: $1" >&2
            exit 2
            ;;
    esac
done

if [ ! -d "$SOURCE_DIR" ]; then
    echo "error: $SOURCE_DIR does not exist" >&2
    exit 1
fi

if [ -z "$(find "$SOURCE_DIR" -maxdepth 1 -name '*.md' -print -quit)" ]; then
    echo "error: no Markdown pages in $SOURCE_DIR" >&2
    exit 1
fi

# Derive the wiki URL from the origin remote when it is not overridden.
if [ -z "${DOOPASS_WIKI_URL:-}" ]; then
    origin="$(git -C "$REPO_ROOT" remote get-url origin 2>/dev/null || true)"

    case "$origin" in
        git@github.com:*) DOOPASS_WIKI_URL="git@github.com:${origin#git@github.com:}" ;;
        https://github.com/*) DOOPASS_WIKI_URL="$origin" ;;
        *)
            echo "error: cannot derive the wiki URL from origin '$origin'" >&2
            echo "       set DOOPASS_WIKI_URL=https://github.com/<owner>/<repo>.wiki.git" >&2
            exit 1
            ;;
    esac

    DOOPASS_WIKI_URL="${DOOPASS_WIKI_URL%.git}.wiki.git"
fi

echo "source: $SOURCE_DIR"
echo "wiki:   $DOOPASS_WIKI_URL"
echo "work:   $WORK_DIR"

if [ ! -d "$WORK_DIR/.git" ]; then
    if [ "$DRY_RUN" -eq 1 ]; then
        echo "would clone $DOOPASS_WIKI_URL into $WORK_DIR"
        exit 0
    fi

    if ! git clone --quiet "$DOOPASS_WIKI_URL" "$WORK_DIR"; then
        cat >&2 <<'EOF'

error: could not clone the wiki repository.

GitHub wikis are a separate git repository that only exists after the first
page is created:

  1. open https://github.com/doopath/doopass-next/wiki
  2. choose "Create the first page", save anything (it will be overwritten)
  3. authenticate git for HTTPS:
       gh auth login && gh auth setup-git

Then re-run this script.
EOF
        exit 1
    fi
else
    git -C "$WORK_DIR" fetch --quiet origin
    git -C "$WORK_DIR" reset --quiet --hard origin/HEAD 2>/dev/null ||
        git -C "$WORK_DIR" reset --quiet --hard origin/master
fi

# Mirror the sources: drop stale pages, copy the current ones.
find "$WORK_DIR" -maxdepth 1 -name '*.md' -delete
cp "$SOURCE_DIR"/*.md "$WORK_DIR"/

cd "$WORK_DIR"

if [ -z "$(git status --porcelain)" ]; then
    echo "wiki is already up to date"
    exit 0
fi

git add --all

if [ "$DRY_RUN" -eq 1 ]; then
    echo
    echo "changes that would be published:"
    git status --short
    exit 0
fi

if [ -z "$MESSAGE" ]; then
    MESSAGE="docs(wiki): sync from docs/wiki @ $(git -C "$REPO_ROOT" rev-parse --short HEAD)"
fi

git -c user.name="${GIT_AUTHOR_NAME:-$(git -C "$REPO_ROOT" config user.name || echo doopass)}" \
    -c user.email="${GIT_AUTHOR_EMAIL:-$(git -C "$REPO_ROOT" config user.email || echo doopass@localhost)}" \
    commit --quiet -m "$MESSAGE"

git push --quiet origin HEAD

echo "published $(git rev-list --count HEAD) revisions; latest: $(git log -1 --pretty=%s)"
