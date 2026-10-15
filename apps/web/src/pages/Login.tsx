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
  const [apply, setApply] = useState(false);
  const [name, setName] = useState("");
  const [role, setRole] = useState<"Waiter" | "Kitchen">("Waiter");
  const [sent, setSent] = useState<string | null>(null);

  if (session?.role === "Waiter") return <Navigate to="/floor" replace />;
  if (session?.role === "Kitchen") return <Navigate to="/kitchen" replace />;
  if (session?.role === "Supply") return <Navigate to="/supply" replace />;
  if (session) return <Navigate to="/" replace />;

  async function submit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(null);
    setSent(null);
    try {
      if (apply) {
        const result = await client.register({ login, password, displayName: name, role });
        setSent(result.message);
        setApply(false);
      } else {
        await client.login(login, password);
      }
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Не удалось");
    } finally {
      setPending(false);
    }
  }

  return (
    <main className="login-wrap">
      <form className="login-card form" onSubmit={submit}>
        <h1>{apply ? "Заявка" : "Вход"}</h1>
        <p>{apply ? "Менеджер подтвердит, потом можно войти." : "Склад, стоп-лист и сроки годности."}</p>
        {apply ? (
          <>
            <label>
              Имя
              <input value={name} onChange={(event) => setName(event.target.value)} required />
            </label>
            <label>
              Куда
              <select value={role} onChange={(event) => setRole(event.target.value as "Waiter" | "Kitchen")}>
                <option value="Waiter">Официант</option>
                <option value="Kitchen">Кухня</option>
              </select>
            </label>
          </>
        ) : null}
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
        {error ? <p className="error-text">{error}</p> : sent ? <p className="hint">{sent}</p> : <p className="hint">manager / manager123 · kitchen / kitchen123 · waiter / waiter123 · supply / supply123</p>}
        <button className="btn" disabled={pending}>{pending ? "Секунду…" : apply ? "Отправить заявку" : "Войти"}</button>
        <button type="button" className="btn-ghost" onClick={() => { setApply((current) => !current); setError(null); setSent(null); }}>
          {apply ? "У меня уже есть вход" : "Нет аккаунта — заявка"}
        </button>
      </form>
    </main>
  );
}
