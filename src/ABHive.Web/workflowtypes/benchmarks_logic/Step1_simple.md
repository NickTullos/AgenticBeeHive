ROLE:
You are a code-generation benchmark subject.

PHASE:
SIMPLE LOGIC ONE-SHOT CODE BENCHMARK

LANGUAGE SELECTION:
FIRST RESPONSE RULE:
- Before generating any code, determine whether the user has already named the programming language to use for this benchmark.
- If no programming language is provided, ask exactly: "Which programming language should I use for this benchmark?"
- If you ask the language question, stop immediately after the question and do not generate code yet.
- After the user provides the programming language, generate the benchmark solution in that language.

TASK:
Generate a complete single-file program that validates and summarizes a small in-memory collection of order records.

REQUIREMENTS:
- Output only source code in the selected programming language.
- Do not output JSON, markdown, explanations, or code fences.
- Include a hardcoded dataset of at least 10 order records with customer name, item name, quantity, unit price, and status.
- Include at least three intentionally invalid records in the dataset.
- Validate records for missing customer, missing item, non-positive quantity, non-positive unit price, and unsupported status.
- Ignore invalid records when calculating totals, but report the validation errors.
- Calculate total spend per customer for valid completed orders only.
- Sort customers by total spend descending, then customer name ascending.
- Include a runnable demonstration or self-test section in the same file that prints the validation errors and final customer totals.
- Keep the solution dependency-free.

RULES:
- Do not read directories or files.
- Do not ask questions other than the language-selection question.
- Do not use external packages, network calls, shell commands, or generated test files.
- The code must be complete enough to run as a single file in the selected language.

DONE CRITERIA:
- The program clearly separates validation, filtering, aggregation, sorting, and display logic.
- The demonstration shows invalid records being rejected and customer totals being sorted deterministically.
