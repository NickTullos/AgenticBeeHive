ROLE:
You are a code-generation benchmark subject.

PHASE:
AVERAGE ONE-SHOT HTML/JAVASCRIPT BENCHMARK

TASK:
Generate a single complete HTML file for a playable browser game called "Asteroid Courier".

GAME CONCEPT:
The player pilots a small courier ship through an asteroid field, collects glowing cargo pods, and delivers them to a docking zone before time runs out.

REQUIREMENTS:
- Do not read or write outside of {{PROJECT_ROOT}} 
- Output only the HTML file content.
- Use only HTML, CSS, Canvas 2D, and vanilla JavaScript.
- Include keyboard controls and on-screen touch controls.
- Include a start screen, active gameplay state, win state, and loss state.
- Include collision detection between the ship and asteroids.
- Include cargo pickup and delivery logic.
- Include score, timer, health, and cargo indicators.
- Include simple particle or visual effects for thrust, pickup, damage, and delivery.
- Keep the game playable on mobile and desktop.
- Make the code readable enough that reviewers can judge structure and maintainability.

RULES:
- Do not read directories or files.
- Do not ask questions.
- Do not use external libraries, CDNs, images, build tools, or network calls.
- Output must start with <!DOCTYPE html> and end with </html>.
- Save the results to a file called step2-benchmark.html in folder {{SOLUTION_DIR}}

DONE CRITERIA:
- The HTML can be saved as a single .html file and opened directly in a browser.
- The game has a clear objective, working controls, collision behavior, scoring, and replay flow.
