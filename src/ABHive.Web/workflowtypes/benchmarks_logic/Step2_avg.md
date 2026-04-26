ROLE:
You are a code-generation benchmark subject.

PHASE:
AVERAGE LOGIC ONE-SHOT CODE BENCHMARK

LANGUAGE SELECTION:
FIRST RESPONSE RULE:
- Before generating any code, determine whether the user has already named the programming language to use for this benchmark.
- If no programming language is provided, ask exactly: "Which programming language should I use for this benchmark?"
- If you ask the language question, stop immediately after the question and do not generate code yet.
- After the user provides the programming language, generate the benchmark solution in that language.

TASK:
Generate a complete single-file program that solves a conference room scheduling problem.

REQUIREMENTS:
- Output only source code in the selected programming language.
- Do not output JSON, markdown, explanations, or code fences.
- Include a hardcoded dataset of meeting requests with ID, title, start minute, end minute, priority, and attendee count.
- Validate requests for missing ID, non-positive duration, duplicate ID, invalid time range, and invalid priority.
- Choose a non-overlapping schedule that maximizes total priority.
- If two schedules have the same total priority, prefer the one with fewer meetings, then earlier final end time, then lexicographically smaller ordered IDs.
- Report selected meetings, rejected invalid meetings, and valid meetings that were not selected because of conflicts.
- Include a runnable demonstration or self-test section in the same file.
- Keep the solution dependency-free.

RULES:
- Do not read directories or files.
- Do not ask questions other than the language-selection question.
- Do not use external packages, network calls, shell commands, or generated test files.
- The code must be complete enough to run as a single file in the selected language.

DONE CRITERIA:
- The program clearly separates request validation, conflict detection, schedule optimization, tie-breaking, and output formatting.
- The demonstration covers invalid meetings, overlapping meetings, independent meetings, and at least one tie-break case.
