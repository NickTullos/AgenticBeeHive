(() => {
    const THEME_KEY = "agenticllm.theme";
    const THEMES = ["current", "light", "dark"];

    function normalizeTheme(value) {
        return THEMES.includes(value) ? value : "current";
    }

    function readTheme() {
        try {
            return normalizeTheme(localStorage.getItem(THEME_KEY) || "current");
        } catch {
            return "current";
        }
    }

    function writeTheme(theme) {
        try {
            localStorage.setItem(THEME_KEY, normalizeTheme(theme));
        } catch {
            // Ignore storage errors.
        }
    }

    function applyTheme(theme) {
        const normalized = normalizeTheme(theme);
        document.documentElement.setAttribute("data-theme", normalized);
        window.dispatchEvent(new CustomEvent("agentic-theme-changed", { detail: { theme: normalized } }));
    }

    function syncSwitchers(theme) {
        const switchers = document.querySelectorAll("[data-theme-switcher]");
        switchers.forEach((element) => {
            if (!(element instanceof HTMLSelectElement)) {
                return;
            }

            if (element.options.length === 0) {
                element.innerHTML = `
                    <option value="current">Current Theme</option>
                    <option value="light">Light Theme</option>
                    <option value="dark">Dark Theme</option>
                `;
            }

            element.value = theme;
        });
    }

    function initializeTheme() {
        const theme = readTheme();
        applyTheme(theme);
        syncSwitchers(theme);

        const switchers = document.querySelectorAll("[data-theme-switcher]");
        switchers.forEach((element) => {
            if (!(element instanceof HTMLSelectElement)) {
                return;
            }

            element.addEventListener("change", () => {
                const selectedTheme = normalizeTheme(element.value);
                writeTheme(selectedTheme);
                applyTheme(selectedTheme);
                syncSwitchers(selectedTheme);
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initializeTheme);
    } else {
        initializeTheme();
    }
})();
