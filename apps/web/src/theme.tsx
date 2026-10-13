import { createContext, useCallback, useContext, useMemo, type MouseEvent, type ReactNode } from "react";
import { flushSync } from "react-dom";

const storageKey = "monitoring-theme";

type Theme = "light" | "dark";

type Store = {
  toggleTheme: (event?: MouseEvent<HTMLElement>) => void;
};

const ThemeContext = createContext<Store | null>(null);

function readTheme(): Theme {
  return document.documentElement.classList.contains("dark") ? "dark" : "light";
}

function applyTheme(theme: Theme) {
  document.documentElement.classList.toggle("dark", theme === "dark");
  localStorage.setItem(storageKey, theme);
}

type ViewTransition = {
  ready: Promise<void>;
};

async function revealFrom(event: MouseEvent<HTMLElement> | undefined, next: () => void) {
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const start = (document as Document & { startViewTransition?: (update: () => void) => ViewTransition }).startViewTransition?.bind(document);
  if (!start || reduced) {
    next();
    return;
  }

  const x = event?.clientX ?? window.innerWidth - 24;
  const y = event?.clientY ?? window.innerHeight - 24;
  const endRadius = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y));
  const transition = start(() => {
    flushSync(next);
  });
  await transition.ready;
  document.documentElement.animate(
    { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${endRadius}px at ${x}px ${y}px)`] },
    { duration: 480, easing: "ease-in", fill: "both", pseudoElement: "::view-transition-new(root)" },
  );
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const toggleTheme = useCallback((event?: MouseEvent<HTMLElement>) => {
    void revealFrom(event, () => {
      applyTheme(readTheme() === "dark" ? "light" : "dark");
    });
  }, []);

  const store = useMemo(() => ({ toggleTheme }), [toggleTheme]);
  return <ThemeContext.Provider value={store}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const value = useContext(ThemeContext);
  if (!value) throw new Error("Тема не инициализирована");
  return value;
}

export function ThemeToggle({ compact = false }: { compact?: boolean }) {
  const { toggleTheme } = useTheme();
  return (
    <button
      type="button"
      className={compact ? "theme-toggle theme-toggle-compact" : "theme-toggle"}
      aria-label="Переключить тему"
      title="Переключить тему"
      onClick={(event) => toggleTheme(event)}
    >
      <svg className="theme-icon theme-icon-sun" viewBox="0 0 24 24" aria-hidden="true">
        <path fill="currentColor" d="M12 4.2a.9.9 0 0 1 .9.9v1.2a.9.9 0 1 1-1.8 0V5.1a.9.9 0 0 1 .9-.9Zm0 12.6a4.8 4.8 0 1 1 0-9.6 4.8 4.8 0 0 1 0 9.6Zm7.1-5.7h1.2a.9.9 0 1 1 0 1.8h-1.2a.9.9 0 1 1 0-1.8ZM4.5 11.1H3.3a.9.9 0 1 0 0 1.8h1.2a.9.9 0 1 0 0-1.8Zm12.7-5.3.85-.85a.9.9 0 1 1 1.27 1.27l-.85.85a.9.9 0 1 1-1.27-1.27ZM5.68 16.9l-.85.85a.9.9 0 1 0 1.27 1.27l.85-.85A.9.9 0 1 0 5.68 16.9Zm12.47 0a.9.9 0 0 1 1.27 0l.85.85a.9.9 0 1 1-1.27 1.27l-.85-.85a.9.9 0 0 1 0-1.27ZM4.83 4.92a.9.9 0 0 1 1.27 0l.85.85A.9.9 0 1 1 5.68 7.04l-.85-.85a.9.9 0 0 1 0-1.27ZM12 17.7a.9.9 0 0 1 .9.9v1.2a.9.9 0 1 1-1.8 0v-1.2a.9.9 0 0 1 .9-.9Z" />
      </svg>
      <svg className="theme-icon theme-icon-moon" viewBox="0 0 24 24" aria-hidden="true">
        <path fill="currentColor" d="M15.2 3.1a.8.8 0 0 1 1 .98 8.2 8.2 0 1 0 3.72 3.72.8.8 0 0 1 .98-1A9.8 9.8 0 1 1 15.2 3.1Z" />
      </svg>
    </button>
  );
}
