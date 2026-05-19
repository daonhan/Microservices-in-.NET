#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import unicodedata
from datetime import datetime, timezone
from pathlib import Path
from typing import NamedTuple


PROJECT_NAME = "nhamnhi"
REDACTION_MARKER = "[REDACTED]"


class TranscriptExtractionError(Exception):
    """Raised when a transcript cannot be converted into readable markdown."""


class HookInputError(Exception):
    """Raised when Claude Code hook input is missing required fields."""


class Turn(NamedTuple):
    role: str
    text: str


SYSTEM_REMINDER_RE = re.compile(r"<system-reminder>.*?</system-reminder>", re.IGNORECASE | re.DOTALL)
JWT_RE = re.compile(r"\b[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b")
AWS_ACCESS_KEY_RE = re.compile(r"\bAKIA[0-9A-Z]{16}\b")
PASSWORD_ASSIGNMENT_RE = re.compile(r"(?i)\b(password\s*=\s*)\S+")
BEARER_HEADER_RE = re.compile(r"(?im)^(\s*Authorization\s*:\s*Bearer\s+)\S+")
PRIVATE_KEY_RE = re.compile(
    r"-----BEGIN (?:RSA |EC )?PRIVATE KEY-----.*?-----END (?:RSA |EC )?PRIVATE KEY-----",
    re.DOTALL,
)


def extract(transcript_path: str | Path) -> str:
    """Return readable conversation markdown from a Claude Code JSONL transcript."""
    return render_turns(extract_turns(transcript_path))


def extract_turns(transcript_path: str | Path) -> list[Turn]:
    path = normalize_path(transcript_path)
    turns: list[Turn] = []
    records_seen = 0

    try:
        with path.open("r", encoding="utf-8") as transcript:
            for line_number, raw_line in enumerate(transcript, start=1):
                if not raw_line.strip():
                    continue

                records_seen += 1
                try:
                    record = json.loads(raw_line)
                except json.JSONDecodeError as exc:
                    raise TranscriptExtractionError(f"Invalid JSON on line {line_number}: {exc}") from exc

                role = _record_role(record)
                if role not in {"user", "assistant"}:
                    continue

                text = _record_text(record)
                if text:
                    turns.append(Turn(role=role, text=text))
    except OSError as exc:
        raise TranscriptExtractionError(f"Could not read transcript '{path}': {exc}") from exc

    if records_seen == 0:
        raise TranscriptExtractionError(f"Transcript '{path}' is empty")

    if not turns:
        raise TranscriptExtractionError(f"Transcript '{path}' contains no user or assistant text")

    return turns


def render_turns(turns: list[Turn]) -> str:
    sections: list[str] = []
    for turn in turns:
        heading = "User" if turn.role == "user" else "Assistant"
        sections.append(f"### {heading}\n\n{turn.text.strip()}")

    return "\n\n".join(sections).rstrip() + "\n"


def redact(text: str) -> str:
    """Best-effort redaction for obvious secrets. This is not a security boundary."""
    redacted = PRIVATE_KEY_RE.sub(REDACTION_MARKER, text)
    redacted = BEARER_HEADER_RE.sub(lambda match: f"{match.group(1)}{REDACTION_MARKER}", redacted)
    redacted = JWT_RE.sub(REDACTION_MARKER, redacted)
    redacted = AWS_ACCESS_KEY_RE.sub(REDACTION_MARKER, redacted)
    redacted = PASSWORD_ASSIGNMENT_RE.sub(lambda match: f"{match.group(1)}{REDACTION_MARKER}", redacted)
    return redacted


def write_session(
    transcript_path: str | Path,
    sessions_dir: str | Path,
    session_id: str,
    started_at: str | None = None,
    branch: str | None = None,
) -> Path:
    transcript = normalize_path(transcript_path)
    turns = extract_turns(transcript)
    conversation = redact(render_turns(turns))
    safe_session_id = sanitize_session_id(session_id)
    started_at_value = started_at or first_transcript_timestamp(transcript) or datetime.now(timezone.utc).isoformat()
    date_value = date_from_timestamp(started_at_value)
    slug = slugify(redact(first_user_text(turns)))

    destination = Path(sessions_dir)
    destination.mkdir(parents=True, exist_ok=True)
    output_path = destination / f"{date_value}-{slug}-{safe_session_id}.md"

    header_lines = [
        f"# Session {safe_session_id}",
        "",
        f"- Date: {started_at_value}",
        f"- Project: {PROJECT_NAME}",
        f"- Session id: {safe_session_id}",
    ]
    if branch:
        header_lines.append(f"- Branch: {branch}")

    body = "\n".join(header_lines) + "\n\n## Conversation\n\n" + conversation
    output_path.write_text(body, encoding="utf-8")
    return output_path


def first_transcript_timestamp(transcript_path: str | Path) -> str | None:
    try:
        with Path(transcript_path).open("r", encoding="utf-8") as transcript:
            for raw_line in transcript:
                if not raw_line.strip():
                    continue
                record = json.loads(raw_line)
                timestamp = record.get("timestamp")
                if isinstance(timestamp, str) and timestamp.strip():
                    return timestamp.strip()
    except (OSError, json.JSONDecodeError):
        return None

    return None


def date_from_timestamp(timestamp: str) -> str:
    match = re.match(r"^(\d{4}-\d{2}-\d{2})", timestamp)
    if match:
        return match.group(1)

    return datetime.now(timezone.utc).date().isoformat()


def first_user_text(turns: list[Turn]) -> str:
    for turn in turns:
        if turn.role == "user":
            return turn.text

    return turns[0].text


def slugify(text: str, max_length: int = 60) -> str:
    ascii_text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode("ascii")
    slug = re.sub(r"[^a-z0-9]+", "-", ascii_text.lower()).strip("-")
    slug = slug[:max_length].strip("-")
    return slug or "session"


def sanitize_session_id(session_id: str) -> str:
    safe = re.sub(r"[^A-Za-z0-9_-]+", "", session_id).strip("-_")
    return safe or "unknown-session"


def normalize_path(path_value: str | Path) -> Path:
    path_text = str(path_value)
    path = Path(path_text).expanduser()
    if path.exists():
        return path

    drive_match = re.match(r"^([A-Za-z]):[\\/](.*)$", path_text)
    if not drive_match:
        return path

    drive = drive_match.group(1).lower()
    remainder = drive_match.group(2).replace("\\", "/")
    for root in (Path(f"/{drive}"), Path(f"/mnt/{drive}")):
        candidate = root / remainder
        if candidate.exists():
            return candidate

    return path


def current_git_branch(repo_root: str | Path) -> str | None:
    repo_path = normalize_path(repo_root)
    try:
        result = subprocess.run(
            ["git", "-C", str(repo_path), "branch", "--show-current"],
            check=False,
            capture_output=True,
            text=True,
            timeout=5,
        )
    except (OSError, subprocess.SubprocessError):
        return None

    branch = result.stdout.strip()
    return branch or None


def _record_role(record: dict) -> str | None:
    message = record.get("message")
    if isinstance(message, dict):
        role = message.get("role")
        if role in {"user", "assistant"}:
            return role

    role = record.get("role") or record.get("type")
    if role in {"user", "assistant"}:
        return role

    return None


def _record_text(record: dict) -> str:
    message = record.get("message")
    content = message.get("content") if isinstance(message, dict) else record.get("content")
    parts = list(_text_parts(content))
    clean_parts = [_strip_system_reminders(part).strip() for part in parts]
    return "\n\n".join(part for part in clean_parts if part)


def _text_parts(content) -> list[str]:
    if isinstance(content, str):
        return [content]

    if not isinstance(content, list):
        return []

    parts: list[str] = []
    for block in content:
        if isinstance(block, str):
            parts.append(block)
            continue

        if not isinstance(block, dict) or block.get("type") != "text":
            continue

        text = block.get("text")
        if isinstance(text, str):
            parts.append(text)

    return parts


def _strip_system_reminders(text: str) -> str:
    return SYSTEM_REMINDER_RE.sub("", text)


def write_session_from_hook(stdin_text: str, repo_root: str | Path) -> Path:
    try:
        hook_input = json.loads(stdin_text)
    except json.JSONDecodeError as exc:
        raise HookInputError(f"Hook stdin was not valid JSON: {exc}") from exc

    transcript_path = hook_input.get("transcript_path")
    if not isinstance(transcript_path, str) or not transcript_path.strip():
        raise HookInputError("SessionEnd hook input is missing 'transcript_path'")

    session_id = hook_input.get("session_id")
    if not isinstance(session_id, str) or not session_id.strip():
        session_id = Path(transcript_path).stem

    repo_path = normalize_path(repo_root)
    sessions_dir = repo_path / ".claude" / "agent-memory" / "sessions"
    branch = current_git_branch(repo_path)
    normalized_transcript_path = normalize_path(transcript_path)
    return write_session(
        transcript_path=normalized_transcript_path,
        sessions_dir=sessions_dir,
        session_id=session_id,
        started_at=first_transcript_timestamp(normalized_transcript_path),
        branch=branch,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="QMD memory helpers for Claude Code transcripts.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    extract_parser = subparsers.add_parser("extract", help="Print cleaned conversation markdown from a transcript.")
    extract_parser.add_argument("transcript_path")

    write_parser = subparsers.add_parser("write-session", help="Read SessionEnd hook JSON from stdin and write markdown.")
    write_parser.add_argument(
        "--repo-root",
        default=os.environ.get("CLAUDE_PROJECT_DIR") or Path.cwd(),
        help="Project root containing .claude/agent-memory/sessions.",
    )

    args = parser.parse_args(argv)

    try:
        if args.command == "extract":
            print(extract(args.transcript_path), end="")
            return 0

        if args.command == "write-session":
            output_path = write_session_from_hook(sys.stdin.read(), repo_root=args.repo_root)
            print(output_path)
            return 0
    except (HookInputError, TranscriptExtractionError) as exc:
        print(f"qmd-memory: {exc}", file=sys.stderr)
        return 1

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
