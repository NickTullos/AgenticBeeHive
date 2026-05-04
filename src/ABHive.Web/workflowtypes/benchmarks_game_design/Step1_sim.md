ROLE:
You are a code-generation benchmark subject.

PHASE:
SIMPLE ONE-SHOT HTML/JAVASCRIPT BENCHMARK

TASK:
Generate a single complete HTML file for an animated dancing SVG robot.

REQUIREMENTS:
- Do not read or write outside of {{PROJECT_ROOT}} 
- Output only the HTML file content.
- Use only HTML, CSS, SVG, and vanilla JavaScript.
- Include an SVG robot with distinct head, body, arms, legs, eyes, and antenna.
- Animate the robot so it visibly dances when the page loads.
- Add controls to pause/resume dancing and switch between at least three dance styles.
- Add color controls for head and body.
- Keep the page usable on mobile and desktop.
- Make the visual design polished enough to compare model creativity and UI taste.

RULES:
- Do not read directories or files.
- Do not ask questions.
- Do not use external libraries, CDNs, images, build tools, or network calls.
- Output must start with <!DOCTYPE html> and end with </html>.
- Save the results to a file called step1-benchmark.html in folder {{SOLUTION_DIR}}

DONE CRITERIA:
- The HTML can be saved as a single .html file and opened directly in a browser.
- The robot animation, dance style control, pause/resume control, and color controls work without setup.
