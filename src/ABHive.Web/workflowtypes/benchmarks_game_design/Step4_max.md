ROLE:
You are a code-generation benchmark subject.

PHASE:
MAX ONE-SHOT HTML/JAVASCRIPT 3D GAME BENCHMARK

TASK:
Generate a single complete HTML file for a playable 3D browser game called "Nick on Bourbon Street".

GAME CONCEPT:
The player controls a human named Nick walking down Bourbon Street in New Orleans, Louisiana. Bourbon Street is an open explorable street corridor with side alleys, balconies, neon signs, jazz clubs, street lights, beads, and French Quarter-inspired buildings, but the playable world is limited to Bourbon Street. Alligators roam the street and try to nip at Nick's ankles. The player must explore, dodge lunging alligators, collect safe-route markers, and reach the end of the street before ankle health runs out.

REQUIREMENTS:
- Do not read or write outside of {{PROJECT_ROOT}} 
- Output only the HTML file content.
- Use only HTML, CSS, WebGL or Canvas-based 3D rendering, and vanilla JavaScript.
- Do not use Three.js, Babylon.js, external libraries, CDNs, images, build tools, or network calls.
- Create a real-time 3D or highly convincing pseudo-3D world with camera movement, depth, scaling, occlusion, and perspective.
- Include a visible player character named Nick with a human silhouette, walking animation, and name label.
- Build a recognizable fictionalized Bourbon Street scene using generated geometry or drawing code: balconies, wrought-iron railings, neon signs, street lamps, music-note/jazz details, cross streets or alleys, and street boundaries.
- Keep the game open-world within the Bourbon Street corridor: the player can move forward, backward, and side-to-side, but cannot leave the street boundary.
- Include multiple alligators that patrol, detect Nick, chase, lunge, snap at ankle height, and temporarily retreat after missing or hitting.
- Include collision detection between Nick, alligators, buildings, street boundaries, and collectible safe-route markers.
- Include ankle health, distance traveled, collected marker count, timer, score, and current objective in the HUD.
- Include start screen, active gameplay state, pause state, win state, loss state, and replay flow.
- Include keyboard controls and on-screen touch controls for mobile.
- Include camera behavior suitable for the game, such as third-person follow, over-the-shoulder, or adjustable chase camera.
- Include animated effects such as street glow, alligator bite warning, dust splashes, screen shake, damage flash, collectible sparkles, or night-life lighting.
- Include simple procedural or seeded placement of buildings, signs, collectibles, and alligator patrol routes so the street feels varied while staying deterministic for review.
- Include accessibility-friendly instructions and clear visual feedback when Nick is in danger.
- Keep all code in the one HTML file and structure it clearly enough that reviewers can judge architecture, gameplay logic, rendering, and maintainability.

RULES:
- Do not read directories or files.
- Do not ask questions.
- Do not use external libraries, CDNs, images, build tools, or network calls.
- Output must start with <!DOCTYPE html> and end with </html>.
- Save the results to a file called step4-benchmark.html in folder {{SOLUTION_DIR}}

DONE CRITERIA:
- The HTML can be saved as a single .html file and opened directly in a browser.
- The game has a clear 3D or pseudo-3D presentation, explorable Bourbon Street boundaries, working movement controls, alligator chase/lunge behavior, ankle-health damage, collectibles, scoring, win/loss conditions, and replay flow.
- The result is visually rich enough to compare model creativity, technical ambition, game feel, and code organization.
