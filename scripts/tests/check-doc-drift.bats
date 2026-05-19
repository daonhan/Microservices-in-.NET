#!/usr/bin/env bats
# Test names follow Given_When_Then. Run: bats scripts/tests/check-doc-drift.bats

setup() {
    SCRIPT_PATH="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/check-doc-drift.sh"
    FIXTURE_ROOT="$(mktemp -d -t doc-drift-XXXXXX)"
    seed_clean_fixture "$FIXTURE_ROOT"
}

teardown() {
    if [[ -n "${FIXTURE_ROOT:-}" && -d "$FIXTURE_ROOT" ]]; then
        rm -rf "$FIXTURE_ROOT"
    fi
}

write_compose() {
    local dest=$1
    cat > "$dest/docker-compose.yaml" <<'YAML'
services:
  product:
    build:
      context: .
      dockerfile: ./product-microservice/Dockerfile
    ports:
      - "8002:8080"
  order:
    build:
      context: .
      dockerfile: ./order-microservice/Dockerfile
    ports:
      - "8001:8080"
  basket:
    build:
      context: .
      dockerfile: ./basket-microservice/Dockerfile
    ports:
      - "8000:8080"
  inventory:
    build:
      context: .
      dockerfile: ./inventory-microservice/Dockerfile
    ports:
      - "8005:8080"
  shipping:
    build:
      context: .
      dockerfile: ./shipping-microservice/Dockerfile
    ports:
      - "8006:8080"
  payment:
    build:
      context: .
      dockerfile: ./payment-microservice/Dockerfile
    ports:
      - "8007:8080"
  saga:
    build:
      context: .
      dockerfile: ./saga-microservice/Dockerfile
    ports:
      - "8008:8080"
  auth:
    build:
      context: .
      dockerfile: ./auth-microservice/Dockerfile
    ports:
      - "8003:8080"
  gateway:
    build:
      context: .
      dockerfile: ./api-gateway/Dockerfile
    ports:
      - "8004:8080"
  rabbitmq:
    image: rabbitmq:3
    ports:
      - "5672:5672"
YAML
}

write_full_catalog() {
    local path=$1
    cat > "$path" <<'MD'
# Catalog

| Service | Port |
|---------|------|
| basket | 8000 |
| order | 8001 |
| product | 8002 |
| auth | 8003 |
| gateway | 8004 |
| inventory | 8005 |
| shipping | 8006 |
| payment | 8007 |
| saga | 8008 |
MD
}

write_catalog_missing_saga() {
    local path=$1
    cat > "$path" <<'MD'
# Catalog

| Service | Port |
|---------|------|
| basket | 8000 |
| order | 8001 |
| product | 8002 |
| auth | 8003 |
| gateway | 8004 |
| inventory | 8005 |
| shipping | 8006 |
| payment | 8007 |
MD
}

seed_clean_fixture() {
    local root=$1
    mkdir -p "$root/.github" "$root/scripts"
    write_compose "$root"
    write_full_catalog "$root/README.md"
    write_full_catalog "$root/CONTEXT.md"
    write_full_catalog "$root/AGENTS.md"
    write_full_catalog "$root/CLAUDE.md"
    write_full_catalog "$root/.github/copilot-instructions.md"
    printf '# allowlist\n' > "$root/scripts/doc-drift-allowlist.txt"
}

@test "Given_Clean_Repo_When_Script_Runs_Then_Exit_Zero" {
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -eq 0 ]
}

@test "Given_Banned_Phrase_Outside_Allowlist_When_Script_Runs_Then_Exit_Non_Zero" {
    mkdir -p "$FIXTURE_ROOT/docs"
    printf 'This describes saga choreography across services.\n' > "$FIXTURE_ROOT/docs/notes.md"
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -ne 0 ]
    [[ "$output" == *"docs/notes.md"* ]]
}

@test "Given_Banned_Phrase_Inside_Allowlist_When_Script_Runs_Then_Exit_Zero" {
    mkdir -p "$FIXTURE_ROOT/docs"
    printf 'This describes saga choreography across services.\n' > "$FIXTURE_ROOT/docs/notes.md"
    printf 'docs/notes.md\n' > "$FIXTURE_ROOT/scripts/doc-drift-allowlist.txt"
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -eq 0 ]
}

@test "Given_Full_Eight_Service_Table_When_Script_Runs_Then_Exit_Zero" {
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -eq 0 ]
}

@test "Given_Catalog_Missing_8008_Row_When_Script_Runs_Then_Exit_Non_Zero" {
    write_catalog_missing_saga "$FIXTURE_ROOT/README.md"
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -ne 0 ]
    [[ "$output" == *"8008"* ]]
    [[ "$output" == *"README.md"* ]]
}

@test "Given_Banned_Phrase_In_Link_Url_When_Script_Runs_Then_Not_Flagged" {
    mkdir -p "$FIXTURE_ROOT/docs"
    printf 'See [ADR-0008](docs/adr/0008-saga-choreography.md) for history.\n' > "$FIXTURE_ROOT/docs/notes.md"
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -eq 0 ]
}

@test "Given_Banned_Phrase_In_Inline_Code_When_Script_Runs_Then_Not_Flagged" {
    mkdir -p "$FIXTURE_ROOT/docs"
    printf 'Filename `0008-saga-choreography.md` is historical.\n' > "$FIXTURE_ROOT/docs/notes.md"
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -eq 0 ]
}

@test "Given_Each_Banned_Phrase_Variant_When_Script_Runs_Then_All_Flagged" {
    mkdir -p "$FIXTURE_ROOT/docs"
    printf 'choreography is bad\n' > "$FIXTURE_ROOT/docs/a.md"
    printf 'no central orchestrator\n' > "$FIXTURE_ROOT/docs/b.md"
    printf 'no orchestrator at all\n' > "$FIXTURE_ROOT/docs/c.md"
    printf 'saga choreography\n' > "$FIXTURE_ROOT/docs/d.md"
    run bash "$SCRIPT_PATH" --root "$FIXTURE_ROOT"
    [ "$status" -ne 0 ]
    [[ "$output" == *"docs/a.md"* ]]
    [[ "$output" == *"docs/b.md"* ]]
    [[ "$output" == *"docs/c.md"* ]]
    [[ "$output" == *"docs/d.md"* ]]
}
