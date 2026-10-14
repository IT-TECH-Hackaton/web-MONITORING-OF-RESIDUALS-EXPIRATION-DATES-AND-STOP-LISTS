import { ApiError, newId, stopWhy, type StopEntry } from "@repo/contracts";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useCart } from "../cart";
import { useSession } from "../session";
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

type SpeechPiece = { readonly isFinal: boolean; readonly 0: { transcript: string } };
type SpeechEvent = { readonly resultIndex: number; readonly results: ArrayLike<SpeechPiece> };
type SpeechEngine = {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  onresult: ((event: SpeechEvent) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
  start: () => void;
  stop: () => void;
  abort: () => void;
};

function speechEngine() {
  const host = window as Window & {
    SpeechRecognition?: new () => SpeechEngine;
    webkitSpeechRecognition?: new () => SpeechEngine;
  };
  return host.SpeechRecognition ?? host.webkitSpeechRecognition ?? null;
}

const russianModelUrl = "/models/vosk-model-small-ru-0.22.tar.gz";
let russianModel: Promise<import("vosk-browser").VoskModel> | null = null;

function FloorListen({ onSave }: { onSave: (text: string) => void }) {
  const [listening, setListening] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const finalText = useRef("");
  const stopListen = useRef<(() => void) | null>(null);

  useEffect(() => () => stopListen.current?.(), []);

  function remember(chunk: string, partial = "") {
    const said = chunk.trim();
    if (said) finalText.current = `${finalText.current} ${said}`.trim();
    setText(`${finalText.current} ${partial}`.trim());
  }

  function stop() {
    stopListen.current?.();
    stopListen.current = null;
    setListening(false);
    setStatus(null);
  }

  function startNative(Ctor: new () => SpeechEngine) {
    const rec = new Ctor();
    rec.lang = "ru-RU";
    rec.continuous = true;
    rec.interimResults = true;
    rec.onresult = (event) => {
      let interim = "";
      for (let index = event.resultIndex; index < event.results.length; index += 1) {
        const piece = event.results[index][0].transcript;
        if (event.results[index].isFinal) remember(piece);
        else interim += piece;
      }
      if (interim) setText(`${finalText.current} ${interim}`.trim());
    };
    rec.onerror = (event) => {
      if (event.error === "aborted" || event.error === "no-speech") return;
      setError(event.error === "not-allowed" ? "Разрешите микрофон в браузере." : "Микрофон не ответил.");
      setListening(false);
    };
    rec.onend = () => setListening(false);
    stopListen.current = () => rec.abort();
    setError(null);
    setListening(true);
    rec.start();
  }

  async function startFallback() {
    setStatus("Загружаю словарь речи…");
    setListening(true);
    setError(null);
    const { createModel } = await import("vosk-browser");
    russianModel ??= createModel(russianModelUrl);
    const model = await russianModel;
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 },
    });
    const audio = new AudioContext();
    const recognizer = new model.KaldiRecognizer(audio.sampleRate);
    recognizer.on("result", (message) => remember(message.result.text));
    recognizer.on("partialresult", (message) => setText(`${finalText.current} ${message.result.partial}`.trim()));
    const source = audio.createMediaStreamSource(stream);
    const node = audio.createScriptProcessor(4096, 1, 1);
    node.onaudioprocess = (event) => {
      try {
        recognizer.acceptWaveform(event.inputBuffer);
      } catch {
        /* кусок тишины можно пропустить */
      }
    };
    const mute = audio.createGain();
    mute.gain.value = 0;
    source.connect(node);
    node.connect(mute);
    mute.connect(audio.destination);
    stopListen.current = () => {
      recognizer.retrieveFinalResult();
      recognizer.remove();
      node.disconnect();
      source.disconnect();
      mute.disconnect();
      stream.getTracks().forEach((track) => track.stop());
      void audio.close();
    };
    setStatus(null);
  }

  async function start() {
    if (!window.isSecureContext) {
      setError("Микрофон открывается с localhost или https. По этому адресу браузер его прячет.");
      return;
    }
    const Ctor = speechEngine();
    if (Ctor) {
      startNative(Ctor);
      return;
    }
    try {
      await startFallback();
    } catch {
      setListening(false);
      setStatus(null);
      setError("Микрофон не включился. Разрешите доступ или наберите заметку вручную.");
    }
  }

  function save() {
    const note = (finalText.current || text).trim();
    if (!note) return;
    stop();
    onSave(note);
    finalText.current = "";
    setText("");
  }

  return (
    <section className="floor-mic" aria-label="Расшифровка разговора">
      {error ? <p className="error-text">{error}</p> : null}
      {status ? <p className="reason">{status}</p> : null}
      {listening && !status && !text ? <p className="reason">Слушаю…</p> : null}
      {text ? <p className="reason floor-transcript">{text}</p> : null}
      {text ? (
        <button type="button" className="btn-ghost note-add" onClick={save}>В заметку</button>
      ) : null}
      <button
        type="button"
        className={listening ? "floor-mic-btn is-on" : "floor-mic-btn"}
        aria-pressed={listening}
        aria-label={listening ? "Остановить запись" : "Слушать разговор"}
        onClick={() => { if (listening) stop(); else void start(); }}
      >
        <span className="floor-mic-core" />
      </button>
    </section>
  );
}

export function FloorShell() {
  const { session, client } = useSession();
  const cart = useCart();
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
      <FloorListen onSave={(text) => cart.addNote(text)} />
      <nav className="floor-tabs">
        <NavLink to="/floor" end>Зал</NavLink>
        <NavLink to="/floor/order">
          Заказ{cart.count > 0 ? <span className="tab-count">{cart.count}</span> : null}
        </NavLink>
      </nav>
    </div>
  );
}

function StopWhySheet({ entry, onClose }: { entry: StopEntry; onClose: () => void }) {
  const why = stopWhy(entry);
  return (
    <div className="modal-back" onClick={onClose}>
      <div className="shift-sheet floor-stop-sheet" role="dialog" aria-labelledby="stop-why-title" onClick={(event) => event.stopPropagation()}>
        <div>
          <p className="chip-label">Стоп-лист</p>
          <h2 id="stop-why-title">{entry.dishName}</h2>
          <p className="reason">{why.source}</p>
        </div>
        {why.category ? <p className="stop-why-category">{why.category}</p> : null}
        <p className="stop-why-detail">{why.detail}</p>
        <button type="button" className="btn" onClick={onClose}>Понятно</button>
      </div>
    </div>
  );
}

export function FloorMenuPage() {
  const { client } = useSession();
  const cart = useCart();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [query, setQuery] = useState("");
  const [hint, setHint] = useState<string | null>(null);
  const [why, setWhy] = useState<StopEntry | null>(null);
  const dishes = stop.data ?? [];
  const shiftClosed = dishes.length > 0 && dishes.every((entry) => entry.isStopped && entry.reason === "Смена закрыта");
  const stopped = dishes.filter((entry) => entry.isStopped);
  const low = dishes.filter((entry) => isLow(entry));
  const priority = dishes.filter((entry) => entry.isPriority && !entry.isStopped);
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

  function inCart(dishId: string) {
    const line = cart.lines.find((item) => item.kind === "dish" && item.dishId === dishId);
    return line?.kind === "dish" ? line.portions : 0;
  }

  function add(entry: StopEntry) {
    if (entry.isStopped) return;
    const next = inCart(entry.dishId) + 1;
    if (entry.hasLimit && next > entry.portionsPossible) {
      setHint(`${entry.dishName}: на заготовке ${entry.portionsPossible} порц. На ${next} не хватит.`);
      return;
    }
    cart.add(entry.dishId, entry.dishName);
    const left = entry.hasLimit ? entry.portionsPossible - next : null;
    if (left === 0) setHint(`${entry.dishName} в заказ. Последняя порция — на 2 стол не возьмёт.`);
    else if (left != null && isLow(entry)) setHint(`${entry.dishName} в заказ. Осталось ${left} порц.`);
    else setHint(`${entry.dishName} в заказ`);
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
      {shiftClosed ? <p className="hint">Смена закрыта. Зал заказы не берёт.</p> : null}
      <section className="chip-block" aria-label="Стоп-лист">
        <p className="chip-label">Стоп · нажми почему</p>
        <div className="chip-row">
          {stopped.length === 0 ? <p className="reason">Пусто — всё можно брать.</p> : null}
          {stopped.map((entry) => (
            <button
              key={entry.dishId}
              type="button"
              className="chip chip-stop"
              onClick={() => setWhy(entry)}
            >
              {entry.dishName}
            </button>
          ))}
        </div>
      </section>
      <section className="chip-block" aria-label="Мало">
        <p className="chip-label">Мало · сколько осталось</p>
        <div className="chip-row">
          {low.length === 0 ? <p className="reason">Нет блюд на исходе.</p> : null}
          {low.map((entry) => (
            <button
              key={entry.dishId}
              type="button"
              className="chip chip-low"
              onClick={() => add(entry)}
            >
              {entry.dishName} · {entry.portionsPossible}
            </button>
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
                    className={["suggest-item", featured ? "is-priority" : "", isLow(entry) ? "is-low" : ""].filter(Boolean).join(" ")}
                    onClick={() => add(entry)}
                  >
                    <span>
                      <strong>{entry.dishName}</strong>
                      {entry.hasLimit ? <span className="reason">{isLow(entry) ? `Мало · ${entry.portionsPossible} порц.` : `${entry.portionsPossible} порц.`}</span> : null}
                    </span>
                    {featured ? <span className="pill pill-priority">приоритет</span> : null}
                    {isLow(entry) ? <span className="pill pill-low">{entry.portionsPossible}</span> : null}
                  </button>
                </li>
              );
            })}
          </ul>
        )
      ) : <p className="reason">Набери блюдо — выбери из списка. Если такого нет, это заметка к заказу.</p>}
      {why ? <StopWhySheet entry={why} onClose={() => setWhy(null)} /> : null}
    </>
  );
}

export function FloorOrderPage() {
  const { client } = useSession();
  const cart = useCart();
  const queryClient = useQueryClient();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [why, setWhy] = useState<StopEntry | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const tableOk = cart.table.trim().length > 0;
  const availability = useMemo(() => new Map((stop.data ?? []).map((entry) => [entry.dishId, entry])), [stop.data]);
  const dishes = cart.lines.filter((line) => line.kind === "dish");
  const blocked = dishes.some((line) => {
    const entry = availability.get(line.dishId);
    return entry != null && (entry.isStopped || (entry.hasLimit && line.portions > entry.portionsPossible));
  });

  async function submit() {
    if (!tableOk || blocked || cart.lines.length === 0) return;
    setPending(true);
    setError(null);
    setDone(null);
    try {
      if (dishes.length > 0) {
        await client.placeOrder({
          id: newId(),
          tableNumber: cart.table.trim(),
          items: dishes.map((line) => ({ dishId: line.dishId, portions: line.portions })),
        });
      }
      const number = cart.table.trim();
      cart.clear();
      setDone(`Касса: стол ${number}, заказ на кухню.`);
      await queryClient.invalidateQueries({ queryKey: ["stop-list"] });
      await queryClient.invalidateQueries({ queryKey: ["priority"] });
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
          const over = entry?.hasLimit === true && line.portions > entry.portionsPossible;
          return (
            <article key={line.dishId} className={stopped || over ? "floor-line floor-line-bad" : "floor-line"}>
              <div>
                <strong>{line.dishName}</strong>
                {stopped ? (
                  <button type="button" className="text-btn stop-why-link" onClick={() => entry && setWhy(entry)}>
                    {entry?.reason ?? "Почему в стопе"}
                  </button>
                ) : null}
                {!stopped && over ? <p className="reason">Стол хочет {line.portions}, на заготовке {entry?.portionsPossible}. Лишнее не отправится.</p> : null}
                {entry && isLow(entry) && !stopped && !over ? <p className="reason">Мало: осталась {entry.portionsPossible} порц.</p> : null}
              </div>
              <div className="stepper">
                <button type="button" onClick={() => cart.change(line.dishId, line.portions - 1)}>−</button>
                <span>{line.portions}</span>
                <button
                  type="button"
                  disabled={entry?.hasLimit === true && line.portions >= entry.portionsPossible}
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
      {why ? <StopWhySheet entry={why} onClose={() => setWhy(null)} /> : null}
    </div>
  );
}
