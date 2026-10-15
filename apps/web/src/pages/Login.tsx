import { ApiError } from "@repo/contracts";
import { useState, type FormEvent } from "react";
import { Navigate } from "react-router-dom";
import { useSession } from "../session";

export function LoginPage() {
  const { session, client } = useSession();
  const [login, setLogin] = useState("manager");
  const [password, setPassword] = useState("manager123");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  if (session?.role === "Waiter") return <Navigate to="/floor" replace />;
  if (session?.role === "Kitchen") return <Navigate to="/kitchen" replace />;
  if (session?.role === "Supply") return <Navigate to="/supply" replace />;
  if (session) return <Navigate to="/" replace />;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(null);
    try {
      await client.login(login, password);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Не удалось войти");
    } finally {
      setPending(false);
    }
  }

  return (
    <main className="login-wrap">
      <form className="login-card form" onSubmit={submit}>
        <h1>Вход</h1>
        <p>Склад, стоп-лист и сроки годности. Учётку выдаёт менеджер.</p>
        <label>
          Логин
          <input value={login} onChange={(event) => setLogin(event.target.value)} autoComplete="username" />
        </label>
        <label>
          Пароль
          <span className="password-field">
            <input
              type={showPassword ? "text" : "password"}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              autoComplete="current-password"
            />
            <button
              type="button"
              className="password-toggle"
              aria-pressed={showPassword}
              aria-label={showPassword ? "Скрыть пароль" : "Показать пароль"}
              onClick={() => setShowPassword((current) => !current)}
            >
              {showPassword ? "Скрыть" : "Показать"}
            </button>
          </span>
        </label>
        {error ? <p className="error-text">{error}</p> : <p className="hint">manager / manager123 · kitchen / kitchen123 · waiter / waiter123 · supply / supply123</p>}
        <button className="btn" disabled={pending}>{pending ? "Входим…" : "Войти"}</button>
      </form>
    </main>
  );
}
