import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "scripts" / "qmd_memory.py"
SPEC = importlib.util.spec_from_file_location("qmd_memory", SCRIPT_PATH)
qmd_memory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(qmd_memory)


class TranscriptExtractorTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def write_transcript(self, records):
        transcript = self.root / "transcript.jsonl"
        transcript.write_text(
            "\n".join(json.dumps(record, ensure_ascii=False) for record in records),
            encoding="utf-8",
        )
        return transcript

    def test_Given_Vanilla_User_And_Assistant_When_Extracting_Then_Markdown_Contains_Text_Turns(self):
        transcript = self.write_transcript(
            [
                {
                    "type": "user",
                    "message": {"role": "user", "content": [{"type": "text", "text": "Remember the YARP decision."}]},
                },
                {
                    "type": "assistant",
                    "message": {"role": "assistant", "content": [{"type": "text", "text": "YARP remains the default gateway provider."}]},
                },
            ]
        )

        markdown = qmd_memory.extract(transcript)

        self.assertIn("### User\n\nRemember the YARP decision.", markdown)
        self.assertIn("### Assistant\n\nYARP remains the default gateway provider.", markdown)

    def test_Given_Tool_Use_And_Thinking_Blocks_When_Extracting_Then_Only_Text_Blocks_Remain(self):
        transcript = self.write_transcript(
            [
                {
                    "type": "user",
                    "message": {
                        "role": "user",
                        "content": [
                            {"type": "text", "text": "Summarize the DLQ poller."},
                            {"type": "tool_result", "content": "tool chatter"},
                        ],
                    },
                },
                {
                    "type": "assistant",
                    "message": {
                        "role": "assistant",
                        "content": [
                            {"type": "thinking", "thinking": "hidden reasoning"},
                            {"type": "tool_use", "name": "Bash", "input": {"command": "rg DLQ"}},
                            {"type": "text", "text": "The gateway owns the operator DLQ poller."},
                        ],
                    },
                },
            ]
        )

        markdown = qmd_memory.extract(transcript)

        self.assertIn("The gateway owns the operator DLQ poller.", markdown)
        self.assertNotIn("tool chatter", markdown)
        self.assertNotIn("hidden reasoning", markdown)
        self.assertNotIn("rg DLQ", markdown)

    def test_Given_System_Reminder_Tags_When_Extracting_Then_Reminders_Are_Stripped(self):
        transcript = self.write_transcript(
            [
                {
                    "type": "user",
                    "message": {
                        "role": "user",
                        "content": [
                            {
                                "type": "text",
                                "text": "Keep this. <system-reminder>Do not save this reminder.</system-reminder> Keep this too.",
                            }
                        ],
                    },
                }
            ]
        )

        markdown = qmd_memory.extract(transcript)

        self.assertIn("Keep this.", markdown)
        self.assertIn("Keep this too.", markdown)
        self.assertNotIn("system-reminder", markdown)
        self.assertNotIn("Do not save this reminder", markdown)

    def test_Given_Mixed_Language_Text_When_Extracting_Then_Unicode_Round_Trips(self):
        transcript = self.write_transcript(
            [
                {
                    "type": "user",
                    "message": {"role": "user", "content": [{"type": "text", "text": "Ghi nhớ quyết định saga strangler."}]},
                },
                {
                    "type": "assistant",
                    "message": {"role": "assistant", "content": [{"type": "text", "text": "Use the strangler path for selected new orders."}]},
                },
            ]
        )

        markdown = qmd_memory.extract(transcript)

        self.assertIn("Ghi nhớ quyết định saga strangler.", markdown)
        self.assertIn("Use the strangler path", markdown)

    def test_Given_Multi_Block_Assistant_Message_When_Extracting_Then_Text_Blocks_Are_Joined(self):
        transcript = self.write_transcript(
            [
                {
                    "type": "assistant",
                    "message": {
                        "role": "assistant",
                        "content": [
                            {"type": "text", "text": "First block."},
                            {"type": "text", "text": "Second block."},
                        ],
                    },
                }
            ]
        )

        markdown = qmd_memory.extract(transcript)

        self.assertIn("First block.\n\nSecond block.", markdown)

    def test_Given_Empty_Or_Corrupt_Transcript_When_Extracting_Then_Raises(self):
        empty = self.root / "empty.jsonl"
        empty.write_text("", encoding="utf-8")
        corrupt = self.root / "corrupt.jsonl"
        corrupt.write_text("{not-json", encoding="utf-8")

        with self.assertRaises(qmd_memory.TranscriptExtractionError):
            qmd_memory.extract(empty)

        with self.assertRaises(qmd_memory.TranscriptExtractionError):
            qmd_memory.extract(corrupt)


class SecretRedactorTests(unittest.TestCase):
    def test_Given_Secret_Patterns_When_Redacting_Then_Secrets_Are_Replaced(self):
        cases = {
            "jwt": "token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c",
            "aws": "aws_key=AKIAABCDEFGHIJKLMNOP",
            "password": "database password=hunter2",
            "bearer": "Authorization: Bearer abcdef1234567890.secret-value",
            "private_key": "-----BEGIN PRIVATE KEY-----\nabc123\n-----END PRIVATE KEY-----",
        }

        for secret_name, text in cases.items():
            with self.subTest(secret_name=secret_name):
                redacted = qmd_memory.redact(text)
                self.assertIn(qmd_memory.REDACTION_MARKER, redacted)
                self.assertNotEqual(text, redacted)

    def test_Given_Non_Secrets_When_Redacting_Then_Text_Round_Trips(self):
        text = (
            "The password policy changed. "
            "Trace id 550e8400-e29b-41d4-a716-446655440000. "
            "Hash e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855."
        )

        self.assertEqual(text, qmd_memory.redact(text))


class SessionWriterTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)

    def test_Given_Transcript_Metadata_When_Writing_Then_File_Name_And_Header_Match_Contract(self):
        transcript = self.root / "transcript.jsonl"
        transcript.write_text(
            json.dumps(
                {
                    "type": "user",
                    "message": {
                        "role": "user",
                        "content": [{"type": "text", "text": "Capture the saga context now with password=hunter2."}],
                    },
                }
            ),
            encoding="utf-8",
        )
        sessions_dir = self.root / "sessions"

        output_path = qmd_memory.write_session(
            transcript_path=transcript,
            sessions_dir=sessions_dir,
            session_id="abc/def",
            started_at="2026-05-18T09:30:00Z",
            branch="main",
        )

        self.assertEqual("2026-05-18-capture-the-saga-context-now-with-password-redacted-abcdef.md", output_path.name)
        markdown = output_path.read_text(encoding="utf-8")
        self.assertIn("# Session abcdef", markdown)
        self.assertIn("- Date: 2026-05-18T09:30:00Z", markdown)
        self.assertIn("- Project: nhamnhi", markdown)
        self.assertIn("- Session id: abcdef", markdown)
        self.assertIn("- Branch: main", markdown)
        self.assertIn("## Conversation", markdown)
        self.assertIn("password=[REDACTED]", markdown)
        self.assertNotIn("hunter2", output_path.name)
        self.assertNotIn("hunter2", markdown)


if __name__ == "__main__":
    unittest.main()
