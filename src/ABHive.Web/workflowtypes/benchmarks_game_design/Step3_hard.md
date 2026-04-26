ROLE:
You are a code-generation benchmark subject.

PHASE:
HARD ONE-SHOT HTML/JAVASCRIPT BENCHMARK

TASK:
Generate a single complete HTML file for a small pseudo-3D browser game called "Neon Drift Arena".

GAME CONCEPT:
The player drives a hover racer around a neon arena, collects energy rings, avoids moving hazards, and survives until the timer ends.

REQUIREMENTS:
- Output only the HTML file content.
- Use only HTML, CSS, Canvas 2D, and vanilla JavaScript.
- Create a convincing pseudo-3D effect using projection, scaling, perspective transforms, or ray/track-style rendering.
- Include keyboard controls and on-screen touch controls.
- Include a start screen, active gameplay state, win state, and loss state.
- Include acceleration, steering, friction, collision detection, collectibles, hazards, score, health, and timer.
- Include animated visual effects such as speed trails, glow, screen shake, particles, or parallax.
- Include simple procedural level/object placement so each run varies slightly.
- Keep the game playable on mobile and desktop.
- Keep all code in the one HTML file and structure it clearly.

RULES:
- Do not read directories or files.
- Do not ask questions.
- Do not use external libraries, CDNs, images, build tools, or network calls.
- Save the results to a file called step3-benchmark.html
- Output must start with <!DOCTYPE html> and end with </html>.

DONE CRITERIA:
- The HTML can be saved as a single .html file and opened directly in a browser.
- The game has a clear loop, visible pseudo-3D presentation, working controls, collision behavior, scoring, and replay flow.
