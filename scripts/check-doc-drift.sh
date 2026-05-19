#!/usr/bin/env bash
# Documentation drift gate for the Nhamnhi monorepo.
#
# Two checks, both run; script exits non-zero if either finds drift.
#
# Check 1 — banned-phrase grep: case-insensitive search over every *.md file in
# the working tree for "choreograph", "no central orchestrator", "no orchestrator",
# and "saga choreography". Paths listed in scripts/doc-drift-allowlist.txt are
# exempt (historical-context docs).
#
# Check 2 — service-table sync: parse docker-compose.yaml, take every service
# whose Dockerfile lives under *-microservice/ or api-gateway/, extract host
# port, then verify each (name, port) appears in the catalog tables in
# README.md, CONTEXT.md, AGENTS.md, CLAUDE.md, and
# .github/copilot-instructions.md.
#
# Failures print as a numbered "N. file:line  reason" list and exit 1.

set -euo pipefail

REPO_ROOT=""
QUIET=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --root)
            REPO_ROOT="$2"
            shift 2
            ;;
        --quiet)
            QUIET=1
            shift
            ;;
        -h|--help)
            sed -n '2,17p' "$0"
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 2
            ;;
    esac
done

if [[ -z "$REPO_ROOT" ]]; then
    REPO_ROOT="$(pwd)"
fi
REPO_ROOT="$(cd "$REPO_ROOT" && pwd)"

ALLOWLIST_PATH="$REPO_ROOT/scripts/doc-drift-allowlist.txt"
COMPOSE_PATH="$REPO_ROOT/docker-compose.yaml"
CATALOG_FILES=(
    "README.md"
    "CONTEXT.md"
    "AGENTS.md"
    "CLAUDE.md"
    ".github/copilot-instructions.md"
)
BANNED_PHRASES=(
    "choreograph"
    "no central orchestrator"
    "no orchestrator"
    "saga choreography"
)

declare -A ALLOWLIST=()
if [[ -f "$ALLOWLIST_PATH" ]]; then
    while IFS= read -r line || [[ -n "$line" ]]; do
        trimmed="${line#"${line%%[![:space:]]*}"}"
        trimmed="${trimmed%"${trimmed##*[![:space:]]}"}"
        [[ -z "$trimmed" ]] && continue
        [[ "${trimmed:0:1}" == "#" ]] && continue
        ALLOWLIST["$trimmed"]=1
    done < "$ALLOWLIST_PATH"
fi

FAILURES=()

# --- Check 1: banned-phrase grep ---
while IFS= read -r -d '' md; do
    rel="${md#$REPO_ROOT/}"
    case "$rel" in
        bin/*|obj/*|node_modules/*|local-nuget-packages/*|.git/*) continue ;;
        */bin/*|*/obj/*|*/node_modules/*) continue ;;
    esac
    if [[ -n "${ALLOWLIST[$rel]:-}" ]]; then
        continue
    fi
    # One sed (strip markdown link URLs and inline code spans) piped into one
    # case-insensitive grep covering all banned phrases. Far faster than
    # invoking sed per line on Windows/Git-Bash.
    while IFS=: read -r line_no rest; do
        [[ -z "$line_no" ]] && continue
        lc="${rest,,}"
        for phrase in "${BANNED_PHRASES[@]}"; do
            if [[ "$lc" == *"${phrase,,}"* ]]; then
                FAILURES+=("${rel}:${line_no}  banned phrase '${phrase}'")
                break
            fi
        done
    done < <(
        sed -E -e 's/\]\([^)]*\)/]/g' -e 's/`[^`]*`//g' "$md" |
            grep -in -E 'choreograph|no central orchestrator|no orchestrator|saga choreography' || true
    )
done < <(find "$REPO_ROOT" -type f -name '*.md' -print0)

# --- Check 2: service-table sync ---
declare -a SVC_NAMES=()
declare -a SVC_PORTS=()

if [[ -f "$COMPOSE_PATH" ]]; then
    current_name=""
    current_dockerfile=""
    current_port=""
    in_services=0

    flush_service() {
        if [[ -n "$current_name" && -n "$current_dockerfile" && -n "$current_port" ]]; then
            if [[ "$current_dockerfile" == *"-microservice/"* || "$current_dockerfile" == *"api-gateway/"* ]]; then
                SVC_NAMES+=("$current_name")
                SVC_PORTS+=("$current_port")
            fi
        fi
    }

    while IFS= read -r ln || [[ -n "$ln" ]]; do
        if [[ "$ln" =~ ^services:[[:space:]]*$ ]]; then
            in_services=1
            continue
        fi
        [[ $in_services -eq 0 ]] && continue

        if [[ "$ln" =~ ^\ \ ([A-Za-z0-9_-]+):[[:space:]]*$ ]]; then
            flush_service
            current_name="${BASH_REMATCH[1]}"
            current_dockerfile=""
            current_port=""
        elif [[ "$ln" =~ ^[A-Za-z0-9_] ]]; then
            flush_service
            current_name=""
            in_services=0
        elif [[ "$ln" =~ dockerfile:[[:space:]]*([^[:space:]]+) ]]; then
            current_dockerfile="${BASH_REMATCH[1]}"
        elif [[ -z "$current_port" && "$ln" =~ -[[:space:]]*\"?([0-9]+):[0-9]+\"? ]]; then
            current_port="${BASH_REMATCH[1]}"
        fi
    done < "$COMPOSE_PATH"
    flush_service

    for i in "${!SVC_NAMES[@]}"; do
        name="${SVC_NAMES[$i]}"
        port="${SVC_PORTS[$i]}"
        name_lc="${name,,}"
        for cat in "${CATALOG_FILES[@]}"; do
            cat_path="$REPO_ROOT/$cat"
            if [[ ! -f "$cat_path" ]]; then
                FAILURES+=("${cat}:0  missing catalog file")
                continue
            fi
            found=0
            while IFS= read -r catln || [[ -n "$catln" ]]; do
                catln_lc="${catln,,}"
                if [[ "$catln_lc" == *"$name_lc"* ]]; then
                    if [[ "$catln" =~ (^|[^0-9])${port}([^0-9]|$) ]]; then
                        found=1
                        break
                    fi
                fi
            done < "$cat_path"
            if [[ $found -eq 0 ]]; then
                FAILURES+=("${cat}:0  missing service '${name}' at port ${port}")
            fi
        done
    done
fi

if [[ ${#FAILURES[@]} -gt 0 ]]; then
    if [[ $QUIET -eq 0 ]]; then
        i=0
        for f in "${FAILURES[@]}"; do
            i=$((i + 1))
            echo "${i}. ${f}"
        done
    fi
    exit 1
fi

if [[ $QUIET -eq 0 ]]; then
    echo "Documentation drift check passed."
fi
exit 0
