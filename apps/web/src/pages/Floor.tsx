import { ApiError, newId, type StopEntry } from "@repo/contracts";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useCart } from "../cart";
import { useSession } from "../session";
import { useShift } from "../shiftStore";
import { ThemeToggle } from "../theme";
import { Empty, isLow } from "../ui";

function dishKey(name: string) {
  return name.trim().toLocaleLowerCase("ru");
}

function matches(name: string, query: string) {
  const needle = dishKey(query);
  if (!needle) return false;
  return dishKey(name).includes(needle);
}

function mergeMenu(api: StopEntry[] | undefined, shift: StopEntry[]): StopEntry[] {
  const map = new Map<string, StopEntry>();
  for (const entry of [...(api ?? []), ...shift]) {
    map.set(dishKey(entry.dishName), entry);
  }
  return [...map.values()];
}

export function FloorShell() {
  const { session, client } = useSession();
  const { count } = useCart();
  const navigate = useNavigate();
  return (
    <div className="floor">
      <header className="floor-head">
        <div>
          <strong>Зал</strong>
          <span>{session?.login}</span>
        </div>
        <div className="floor-head-actions">
          <ThemeToggle compact />
          <button type="button" className="text-btn" onClick={() => { client.logout(); navigate("/login"); }}>Выйти</button>
        </div>
      </header>
      <main className="floor-body">
        <Outlet />
      </main>
      <nav className="floor-tabs">
        <NavLink to="/floor" end>Зал</NavLink>
        <NavLink to="/floor/order">
          Заказ{count > 0 ? <span className="tab-count">{count}</span> : null}
        </NavLink>
      </nav>
    </div>
  );
}

export function FloorMenuPage() {
  const { client } = useSession();
  const cart = useCart();
  const shift = useShift();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [query, setQuery] = useState("");
  const [hint, setHint] = useState<string | null>(null);
  const dishes = useMemo(() => mergeMenu(stop.data, shift.dishes), [stop.data, shift.dishes]);
  const stopped = shift.dishes.filter((entry) => entry.isStopped);
  const priority = shift.dishes.filter((entry) => entry.isPriority && !entry.isStopped);
  const hotKeys = useMemo(() => new Set(priority.map((item) => dishKey(item.dishName))), [priority]);
  const needle = dishKey(query);

  const results = useMemo(() => {
    const found = dishes.filter((entry) => !entry.isStopped && matches(entry.dishName, query));
    return found.sort((a, b) => {
      const aHot = hotKeys.has(dishKey(a.dishName)) ? 0 : 1;
      const bHot = hotKeys.has(dishKey(b.dishName)) ? 0 : 1;
      if (aHot !== bHot) return aHot - bHot;
      const aPref = dishKey(a.dishName).startsWith(needle) ? 0 : 1;
      const bPref = dishKey(b.dishName).startsWith(needle) ? 0 : 1;
      return aPref - bPref || a.dishName.localeCompare(b.dishName, "ru");
    });
  }, [dishes, query, needle, hotKeys]);
  const canNote = Boolean(needle) && results.length === 0;

  function add(entry: StopEntry) {
    if (entry.isStopped) return;
    cart.add(entry.dishId, entry.dishName);
    setHint(`${entry.dishName} в заказ`);
    setQuery("");
  }

  function addNote() {
    const text = query.trim();
    if (!text || results.length > 0) return;
    cart.addNote(text);
    setHint(`Заметка: ${text}`);
    setQuery("");
  }

  return (
    <>
      <section className="chip-block" aria-label="Стоп-лист">
        <p className="chip-label">Стоп</p>
        <div className="chip-row">
          {stopped.map((entry) => (
            <span key={entry.dishId} className="chip chip-stop" title={entry.reason ?? "стоп"}>
              {entry.dishName}
            </span>
          ))}
        </div>
      </section>
      <section className="chip-block" aria-label="Приоритет">
        <p className="chip-label">Предложить</p>
        <div className="chip-row">
          {priority.map((offer) => (
            <button
              key={offer.dishId}
              type="button"
              className="chip chip-priority"
              onClick={() => add(offer)}
            >
              {offer.dishName}
            </button>
          ))}
        </div>
      </section>
      <label className="floor-search">
        <span className="chip-label">Заметка к кассе</span>
        <input
          value={query}
          onChange={(event) => { setQuery(event.target.value); setHint(null); }}
          onKeyDown={(event) => {
            if (event.key === "Enter" && canNote) {
              event.preventDefault();
              addNote();
            }
          }}
          placeholder="Блюдо или заметка: без орехов, аллергия…"
          autoCapitalize="none"
          autoComplete="off"
        />
      </label>
      {canNote ? (
        <button type="button" className="btn-ghost note-add" onClick={addNote}>
          Заметка
        </button>
      ) : null}
      {hint ? <p className="hint">{hint}</p> : null}
      {needle ? (
        results.length === 0 ? (
          <p className="reason">Нет в меню — сохрани как заметку к кассе.</p>
        ) : (
          <ul className="suggest">
            {results.map((entry) => {
              const featured = hotKeys.has(dishKey(entry.dishName));
              return (
                <li key={entry.dishId}>
                  <button
                    type="button"
                    className={["suggest-item", featured ? "is-priority" : ""].filter(Boolean).join(" ")}
                    onClick={() => add(entry)}
                  >
                    <span>
                      <strong>{entry.dishName}</strong>
                    </span>
                    {featured ? <span className="pill pill-priority">приоритет</span> : null}
                  </button>
                </li>
              );
            })}
          </ul>
        )
      ) : <p className="reason">Набери блюдо — выбери из списка. Если такого нет, это заметка к заказу.</p>}
    </>
  );
}

export function FloorOrderPage() {
  const { client } = useSession();
  const cart = useCart();
  const shift = useShift();
  const queryClient = useQueryClient();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const tableOk = cart.table.trim().length > 0;
  const availability = useMemo(() => {
    const map = new Map((stop.data ?? []).map((entry) => [entry.dishId, entry]));
    for (const entry of shift.dishes) map.set(entry.dishId, entry);
    return map;
  }, [stop.data, shift.dishes]);
  const dishes = cart.lines.filter((line) => line.kind === "dish");
  const blocked = dishes.some((line) => {
    const entry = availability.get(line.dishId);
    return entry != null && (entry.isStopped || line.portions > entry.portionsPossible);
  });

  async function submit() {
    if (!tableOk || blocked || cart.lines.length === 0) return;
    setPending(true);
    setError(null);
    setDone(null);
    try {
      const apiLines = dishes.filter((line) => !line.dishId.startsWith("demo-"));
      const demoLines = dishes.filter((line) => line.dishId.startsWith("demo-"));
      if (apiLines.length > 0) {
        await client.placeOrder({
          id: newId(),
          items: apiLines.map((line) => ({ dishId: line.dishId, portions: line.portions })),
        });
      }
      if (demoLines.length > 0) {
        shift.applyOrder(demoLines.map((line) => ({ dishId: line.dishId, portions: line.portions })), "зал");
      }
      const number = cart.table.trim();
      cart.clear();
      setDone(`Касса: стол ${number}, заказ на кухню.`);
      await queryClient.invalidateQueries({ queryKey: ["stop-list"] });
      await queryClient.invalidateQueries({ queryKey: ["hot-offers"] });
    } catch (caught) {
      if (caught instanceof ApiError && caught.order) {
        const lines = caught.order.shortages.map((item) => `${item.productName}: не хватает ${item.deficit}`).join(". ");
        setError(lines || caught.message);
      } else {
        setError(caught instanceof Error ? caught.message : "Касса не отправила заказ");
      }
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="floor-order">
      <p className="floor-lead">Имитация кассы: стол обязателен, затем отправка на кухню. Оплата здесь не берётся.</p>
      <label className="table-field">
        Номер стола
        <input
          inputMode="numeric"
          required
          value={cart.table}
          onChange={(event) => cart.setTable(event.target.value)}
        />
      </label>
      {cart.lines.length === 0 ? <Empty>Заметок нет. Найди блюдо на вкладке «Зал».</Empty> : null}
      <div className="stack">
        {cart.lines.map((line) => {
          if (line.kind === "note") {
            return (
              <article key={line.id} className="floor-line floor-line-note">
                <div>
                  <p className="chip-label">Заметка</p>
                  <strong>{line.text}</strong>
                </div>
                <button type="button" className="text-btn" onClick={() => cart.removeNote(line.id)}>Убрать</button>
              </article>
            );
          }
          const entry = availability.get(line.dishId);
          const stopped = entry?.isStopped === true;
          const over = entry != null && line.portions > entry.portionsPossible;
          return (
            <article key={line.dishId} className={stopped || over ? "floor-line floor-line-bad" : "floor-line"}>
              <div>
                <strong>{line.dishName}</strong>
                {stopped ? <p className="error-text">{entry?.reason}</p> : null}
                {!stopped && over ? <p className="reason">Хватит на {entry?.portionsPossible} порц.</p> : null}
                {entry && isLow(entry) && !stopped ? <p className="reason">Мало на заготовке.</p> : null}
              </div>
              <div className="stepper">
                <button type="button" onClick={() => cart.change(line.dishId, line.portions - 1)}>−</button>
                <span>{line.portions}</span>
                <button
                  type="button"
                  disabled={entry != null && line.portions >= entry.portionsPossible}
                  onClick={() => cart.change(line.dishId, line.portions + 1)}
                >
                  +
                </button>
              </div>
            </article>
          );
        })}
      </div>
      {!tableOk && cart.lines.length > 0 ? <p className="error-text">Укажи номер стола — как на кассе.</p> : null}
      {blocked ? <p className="error-text">В заказе стоп или порций больше, чем есть. Склад при отказе не трогаем.</p> : null}
      {error ? <p className="error-text">{error}</p> : null}
      {done ? <p className="hint">{done}</p> : null}
      <button
        className="btn floor-submit"
        disabled={pending || blocked || cart.lines.length === 0 || !tableOk}
        onClick={() => void submit()}
      >
        {pending ? "Отправляем…" : "Отправить на кухню"}
      </button>
    </div>
  );
}
