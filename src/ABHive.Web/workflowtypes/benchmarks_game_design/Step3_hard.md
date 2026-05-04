PHASE:
HARD ONE-SHOT HTML/JAVASCRIPT BENCHMARK

TASK:
Generate a single, self-contained HTML file for a small pseudo-3D browser game called "Neon Drift Arena". The file must be able to be opened directly in a web browser and should contain all game code within it.

GAME CONCEPT:
In "Neon Drift Arena", the player controls a hover racer that speeds through a neon-lit, futuristic arena. The player must:

Collect energy rings that appear randomly within the arena.
Avoid moving hazards, such as other racers, laser beams, or spinning obstacles.
Survive until the timer runs out. If the player survives, they win; if they crash or fail to collect enough energy rings, they lose.

GAME FEATURES:

Movement and Physics:
Player vehicle: Hover racer with acceleration, steering (left/right), and braking. The racer should have basic friction, gravity-like effects, and collision detection.

Movement mechanics: The hover racer should accelerate when the player presses the gas and decelerate naturally when no key is pressed.
Game state mechanics: There should be a timer that counts down from a set time, score tracking, and health (decreasing on collision with obstacles).

Graphics and Effects:
Pseudo-3D look: Use perspective transforms, projection, scaling, or raycasting techniques to create a convincing 3D-like effect in a 2D canvas.
The game should have animated speed trails, particle effects for collecting items, glow effects on objects, and screen shake on impact or event triggers.
Create randomized level generation: Obstacles and collectible positions should change slightly with each new game session to ensure replayability.
Controls:

Desktop: Use the WASD keys (or arrow keys) for movement and the spacebar or mouse to perform actions (such as collecting rings).

Responsiveness: Ensure that the game works equally well on both desktop and mobile devices.

Game States and Flow:

Start screen: The game should have a title screen that displays the game's name and a "Start" button that launches the game.
Gameplay screen: This is where the player controls the hover racer, collects rings, avoids obstacles, and manages the game timer.
Win and Loss states: Show a "Game Over" screen when the player loses (i.e., crashes or time runs out), displaying the score and a "Retry" button to start a new game.

Gameplay Mechanics:
Accurate collision detection: The hover racer should collide with both walls and dynamic moving objects (e.g., obstacles, other racers).
Hazards: These should move in different patterns and include things like lasers, spinning barriers, or other racers.

Collectibles: Energy rings should appear in random positions within the arena. When collected, they should increase the score.

Score tracking: Keep track of collected energy rings, remaining health, and elapsed time.
Health system: Player loses health on impact with obstacles. When health reaches zero, the game ends.

TECHNICAL REQUIREMENTS:

Technologies: Use only HTML, CSS, Canvas 2D, and vanilla JavaScript.
Game mechanics should include the following elements:
Keyboard controls (WASD/arrows) and mobile touch controls.
Basic physics like acceleration, friction, and steering.
Collision detection between the player and hazards/objects.
Real-time timer and score tracking.
Random level and object generation for replayability.
Mobile and Desktop Support: The game must work well on both mobile and desktop environments.
Game Loop: There should be a clear game loop that handles player input, updates game state, and renders graphics continuously.

RULES:

- Do not read or write outside of {{PROJECT_ROOT}}.
- Output only the HTML file content.
- Use only HTML, CSS, Canvas 2D, and vanilla JavaScript.
- Save the results to a file called step3-benchmark.html in folder {{SOLUTION_DIR}}.
- Output must start with <!DOCTYPE html> and end with </html>.
- The code must be self-contained, and the game must function as expected when the HTML file is opened directly in a web browser.

DONE CRITERIA:

- The HTML file can be opened in a browser and is a fully functional game.
- It has a clear loop, visible pseudo-3D presentation, working controls, collision behavior, scoring, and replay flow.