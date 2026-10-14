import { ApiError, formatHours, formatQty, stopWhy, type Product, type PurchaseSource, type ShiftHistoryEntry, type StopEntry, type StopReason, type WriteOffReason } from "@repo/contracts";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { parsePortionPhrase, type PortionProposal } from "../portionVoice";
import { useSession } from "../session";
import { DishTile, Empty, formatWhen, isLow, Panel } from "../ui";

type Filter = "all" | "stop" | "low" | "candidate" | "priority";

const FILTERS: { id: Filter; label: string }[] = [
  { id: "all", label: "Все" },
  { id: "stop", label: "Стоп" },
  { id: "low", label: "Мало" },
  { id: "candidate", label: "Кандидаты" },
  { id: "priority", label: "Приоритет" },
];

const STOP_REASONS: { id: StopReason; label: string }[] = [
  { id: "OutOfProduct", label: "Закончился продукт" },
  { id: "Waste", label: "Брак" },
  { id: "Quality", label: "Качество" },
  { id: "WontMakeIt", label: "Не успеем" },
  { id: "Equipment", label: "Техника" },
];

const WASTE_REASONS: { id: WriteOffReason; label: string }[] = [
  { id: "Expiry", label: "Срок" },
  { id: "Waste", label: "Брак" },
  { id: "Spoilage", label: "Порча" },
  { id: "Training", label: "Проработка" },
  { id: "StaffMeal", label: "Питание" },
];

function sortShift(dishes: StopEntry[]) {
  return [...dishes].sort((a, b) => {
    const rank = (dish: StopEntry) => {
      if (dish.isStopped) return 0;
      if (dish.isCandidate) return 1;
      if (isLow(dish)) return 2;
      if (dish.isPriority) return 3;
      return 4;
    };
    return rank(a) - rank(b) || a.dishName.localeCompare(b.dishName, "ru");
  });
}

function matchesFilter(dish: StopEntry, filter: Filter) {
  if (filter === "stop") return dish.isStopped;
  if (filter === "low") return isLow(dish);
  if (filter === "candidate") return dish.isCandidate && !dish.isStopped;
  if (filter === "priority") return dish.isPriority && !dish.isStopped;
  return true;
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : "Не получилось сохранить смену";
}

function purchaseWho(source: PurchaseSource) {
  if (source === "Chef") return "кухня";
  if (source === "Forecast") return "прогноз";
  return "менеджер";
}

function NeedOrderPanel({
  products,
  lines,
  loading,
  onAdd,
  onCancel,
  onError,
}: {
  products: Product[];
  lines: { id: string; productName: string; unit: Product["unit"]; quantity: number; note: string | null; source: PurchaseSource; requestedByLogin: string }[];
  loading: boolean;
  onAdd: (productId: string, quantity: number, note: string | null) => Promise<void>;
  onCancel: (id: string) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [productId, setProductId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [note, setNote] = useState("");
  const [pending, setPending] = useState(false);

  async function submit() {
    setPending(true);
    try {
      await onAdd(productId, Number(quantity), note.trim() || null);
      setProductId("");
      setQuantity("");
      setNote("");
    } catch (error) {
      onError(messageOf(error));
    } finally {
      setPending(false);
    }
  }

  return (
    <Panel title="Нужно заказать">
      <p className="reason">Строка уйдёт менеджеру в заказ поставщику. Холодильник не меняется, пока машину не примут.</p>
      {loading ? <p>Загрузка списка…</p> : null}
      {lines.length === 0 && !loading ? <Empty>Пока ничего не просили.</Empty> : (
        <div className="stack">
          {lines.map((line) => (
            <div key={line.id} className="hot-row">
              <span>
                <strong>{line.productName}</strong>
                <p className="reason">
                  {formatQty(line.quantity, line.unit)} · {purchaseWho(line.source)} · {line.requestedByLogin}
                  {line.note ? ` · ${line.note}` : ""}
                </p>
              </span>
              <button type="button" className="btn-ghost" onClick={() => void onCancel(line.id)}>Убрать</button>
            </div>
          ))}
        </div>
      )}
      <form className="form" onSubmit={(event) => { event.preventDefault(); void submit(); }}>
        <label>
          Продукт
          <select value={productId} onChange={(event) => setProductId(event.target.value)} required>
            <option value="">Из холодильника / каталога</option>
            {products.map((product) => (
              <option key={product.id} value={product.id}>{product.name}</option>
            ))}
          </select>
        </label>
        <label>Сколько<input type="number" min="0.001" step="0.001" value={quantity} onChange={(event) => setQuantity(event.target.value)} required /></label>
        <label>Заметка<input value={note} onChange={(event) => setNote(event.target.value)} placeholder="банкет, замена, кончилось…" /></label>
        <button className="btn" disabled={pending}>{pending ? "Пишем…" : "В заказ поставщику"}</button>
      </form>
    </Panel>
  );
}

export function KitchenPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });
  const kitchen = useQuery({ queryKey: ["kitchen"], queryFn: () => client.kitchen() });
  const products = useQuery({ queryKey: ["products"], queryFn: () => client.products() });
  const purchase = useQuery({ queryKey: ["purchase-lines"], queryFn: () => client.purchaseLines(true) });
  const dishes = stop.data ?? [];
  const [openId, setOpenId] = useState<string | null>(null);
  const [wasteOpen, setWasteOpen] = useState(false);
  const [filter, setFilter] = useState<Filter>("all");
  const [query, setQuery] = useState("");
  const [hint, setHint] = useState<string | null>(null);
  const open = dishes.find((dish) => dish.dishId === openId) ?? null;
  const needle = query.trim().toLocaleLowerCase("ru");

  async function refresh() {
    await queryClient.invalidateQueries({ queryKey: ["stop-list"] });
    await queryClient.invalidateQueries({ queryKey: ["priority"] });
    await queryClient.invalidateQueries({ queryKey: ["dashboard"] });
    await queryClient.invalidateQueries({ queryKey: ["kitchen"] });
    await queryClient.invalidateQueries({ queryKey: ["purchase-lines"] });
  }

  async function run(action: () => Promise<unknown>, ok: string) {
    try {
      await action();
      setHint(ok);
      await refresh();
    } catch (error) {
      setHint(messageOf(error));
    }
  }

  const counts = useMemo(() => ({
    all: dishes.length,
    stop: dishes.filter((dish) => dish.isStopped).length,
    low: dishes.filter(isLow).length,
    candidate: dishes.filter((dish) => dish.isCandidate && !dish.isStopped).length,
    priority: dishes.filter((dish) => dish.isPriority && !dish.isStopped).length,
  }), [dishes]);

  const visible = useMemo(() => {
    const found = dishes.filter((dish) => {
      if (!matchesFilter(dish, filter)) return false;
      if (!needle) return true;
      return dish.dishName.toLocaleLowerCase("ru").includes(needle);
    });
    return sortShift(found);
  }, [dishes, filter, needle]);

  const hot = (offers.data ?? [])
    .map((item) => {
      const dish = dishes.find((entry) => entry.dishId === item.dishId);
      if (!dish || dish.isStopped || dish.isPriority) return null;
      return { dish, reason: item.reason };
    })
    .filter((item): item is { dish: StopEntry; reason: string } => item != null)
    .slice(0, 4);

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Смена</h1>
          <p>Стоп зала: шеф, ноль заготовки или склад не тянет на порцию. Кандидат — сырья мало, блюдо ещё открыто. Заказ с зала сам минусует плитку.</p>
        </div>
        <button type="button" className="btn-ghost" onClick={() => setWasteOpen(true)}>Акт списания</button>
      </div>
      {hint ? <p className="hint">{hint}</p> : null}
      <div className="shift-toolbar">
        <div className="shift-filters" role="tablist" aria-label="Фильтр плиток">
          {FILTERS.map((item) => (
            <button
              key={item.id}
              type="button"
              role="tab"
              aria-selected={filter === item.id}
              className={filter === item.id ? "filter-chip is-on" : "filter-chip"}
              onClick={() => setFilter(item.id)}
            >
              {item.label} {counts[item.id]}
            </button>
          ))}
        </div>
        <input
          className="shift-search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Найти блюдо"
          autoComplete="off"
        />
      </div>
      {hot.length > 0 ? (
        <Panel title="Система предлагает в приоритет">
          <p className="reason">Короткий срок партий. Подтверди 2–4 блюда — зал увидит «предлагать». Это не стоп.</p>
          <div className="stack">
            {hot.map((item) => (
              <div key={item.dish.dishId} className="hot-row">
                <span>
                  <strong>{item.dish.dishName}</strong>
                  <p className="reason">{item.reason}</p>
                </span>
                <button type="button" className="btn" onClick={() => void run(() => client.shiftPriority(item.dish.dishId, true), `${item.dish.dishName} в приоритете`)}>
                  В приоритет
                </button>
              </div>
            ))}
          </div>
        </Panel>
      ) : null}
      {stop.isLoading ? <p>Загрузка смены…</p> : null}
      {visible.length === 0 && !stop.isLoading ? <Empty>Нет блюд в этом фильтре.</Empty> : (
        <div className="tiles tiles-shift">
          {visible.map((entry) => (
            <DishTile
              key={entry.dishId}
              entry={entry}
              priority={entry.isPriority && !entry.isStopped}
              candidate={entry.isCandidate && !entry.isStopped}
              showReason={entry.isStopped || Boolean(entry.isCandidate && !entry.isStopped && entry.candidateReason)}
              onClick={() => setOpenId(entry.dishId)}
            />
          ))}
        </div>
      )}
      <Panel title="Использовать первым">
        {(kitchen.data?.batches.length ?? 0) === 0 ? <Empty>Нет коротких и вскрытых партий.</Empty> : (
          <div className="stack">
            {kitchen.data?.batches.slice(0, 4).map((batch) => (
              <button
                key={batch.batchId}
                type="button"
                className={batch.openedAt ? "batch-row is-open" : "batch-row"}
                onClick={() => void run(
                  () => client.markBatch(batch.batchId, true, batch.note ?? "", null),
                  batch.openedAt ? `${batch.productName} уже вскрыта` : `${batch.productName}: вскрыли`,
                )}
              >
                <span>
                  <strong>{batch.productName}</strong>
                  <p className="reason">
                    {batch.deliveryNumber} · {formatQty(batch.remaining, batch.unit)} · {formatHours(batch.hoursLeft)}
                  </p>
                </span>
                <span className={batch.openedAt ? "pill pill-low" : "pill"}>{batch.openedAt ? "вскрыта" : "вскрыть"}</span>
              </button>
            ))}
          </div>
        )}
      </Panel>
      <NeedOrderPanel
        products={products.data ?? []}
        lines={purchase.data ?? []}
        loading={purchase.isLoading}
        onAdd={async (productId, quantity, note) => {
          await client.addPurchaseLine({ productId, quantity, note });
          setHint("Добавили в заказ поставщику. Холодильник не менялся.");
          await queryClient.invalidateQueries({ queryKey: ["purchase-lines"] });
        }}
        onCancel={async (id) => {
          await client.cancelPurchaseLine(id);
          await queryClient.invalidateQueries({ queryKey: ["purchase-lines"] });
        }}
        onError={(message) => setHint(message)}
      />
      {open ? (
        <ShiftSheet
          key={open.dishId}
          dish={open}
          dishes={dishes}
          onClose={() => setOpenId(null)}
          onPortions={(delta) => void run(() => client.shiftPortions(open.dishId, delta), `${open.dishName}: порции ${delta > 0 ? "+" : ""}${delta}`)}
          onStop={(reason, note) => void run(() => client.shiftStop(open.dishId, reason, note), `${open.dishName} в стопе`)}
          onResume={() => void run(() => client.shiftResume(open.dishId), `${open.dishName}: стоп снят`)}
          onPriority={() => void run(() => client.shiftPriority(open.dishId, !open.isPriority), open.isPriority ? "Убрали из приоритета" : "В приоритете")}
        />
      ) : null}
      {wasteOpen ? (
        <WasteSheet
          onClose={() => setWasteOpen(false)}
          onDone={async (reason, items) => {
            await client.createWriteOff({ reason, items });
            setWasteOpen(false);
            setHint("Акт списания проведён");
            await refresh();
          }}
        />
      ) : null}
    </>
  );
}

type SpeechResult = {
  readonly results: ArrayLike<ArrayLike<{ transcript: string }>>;
};

type SpeechEngine = {
  lang: string;
  interimResults: boolean;
  maxAlternatives: number;
  onresult: ((event: SpeechResult) => void) | null;
  onerror: (() => void) | null;
  onend: (() => void) | null;
  start: () => void;
  abort: () => void;
};

function speechEngine() {
  const host = window as Window & {
    SpeechRecognition?: new () => SpeechEngine;
    webkitSpeechRecognition?: new () => SpeechEngine;
  };
  return host.SpeechRecognition ?? host.webkitSpeechRecognition ?? null;
}

function PortionVoice({
  dish,
  dishes,
  onConfirm,
}: {
  dish: StopEntry;
  dishes: StopEntry[];
  onConfirm: (delta: number) => void;
}) {
  const [listening, setListening] = useState(false);
  const [draft, setDraft] = useState("");
  const [proposal, setProposal] = useState<PortionProposal | null>(null);
  const engine = useRef<SpeechEngine | null>(null);

  useEffect(() => {
    setProposal(null);
    setDraft("");
    return () => engine.current?.abort();
  }, [dish.dishId]);

  function consider(heard: string) {
    setProposal(parsePortionPhrase(heard, dishes, dish.dishId));
  }

  function listen() {
    const Ctor = speechEngine();
    if (!Ctor) {
      setProposal({ ok: false, heard: "", reason: "Браузер не слышит микрофон. Напишите фразу — она тоже не применится сама." });
      return;
    }
    engine.current?.abort();
    const rec = new Ctor();
    rec.lang = "ru-RU";
    rec.interimResults = false;
    rec.maxAlternatives = 1;
    rec.onresult = (event) => {
      const heard = event.results[0]?.[0]?.transcript ?? "";
      setDraft(heard);
      consider(heard);
    };
    rec.onerror = () => {
      setListening(false);
      setProposal({ ok: false, heard: draft, reason: "Микрофон не ответил. Ничего не изменил." });
    };
    rec.onend = () => setListening(false);
    engine.current = rec;
    setListening(true);
    setProposal(null);
    rec.start();
  }

  function confirm() {
    if (!proposal?.ok || !proposal.matchesOpen) return;
    onConfirm(proposal.delta);
    setProposal(null);
    setDraft("");
  }

  return (
    <div className="voice-box">
      <div className="voice-row">
        <button type="button" className={listening ? "btn" : "btn-ghost"} aria-pressed={listening} onClick={listen}>
          {listening ? "Слушаю…" : "Микрофон"}
        </button>
        <input
          value={draft}
          placeholder="паста плюс 3"
          aria-label="Фраза для порций"
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              consider(draft);
            }
          }}
        />
        <button type="button" className="btn-ghost" onClick={() => consider(draft)} disabled={!draft.trim()}>
          Разобрать
        </button>
      </div>
      <p className="reason">Фраза только собирает чип. Порции заготовки меняются после «Подтвердить». Это не приход на склад.</p>
      {proposal ? (
        <div className={proposal.ok && proposal.matchesOpen ? "voice-chip" : "voice-chip is-bad"} role="status">
          {proposal.ok ? (
            <>
              <strong>{proposal.dishName} {proposal.delta > 0 ? "+" : ""}{proposal.delta}</strong>
              {proposal.heard ? <p className="reason">Услышали: «{proposal.heard}»</p> : null}
              {proposal.matchesOpen ? (
                <div className="btn-row">
                  <button type="button" className="btn" onClick={confirm}>Подтвердить</button>
                  <button type="button" className="btn-ghost" onClick={() => setProposal(null)}>Не то</button>
                </div>
              ) : (
                <>
                  <p className="reason">Открыта плитка «{dish.dishName}». Чужое блюдо фразой не двигаю.</p>
                  <button type="button" className="btn-ghost" onClick={() => setProposal(null)}>Закрыть</button>
                </>
              )}
            </>
          ) : (
            <>
              <strong>Не разобрал</strong>
              <p className="reason">{proposal.reason}</p>
              {proposal.heard ? <p className="reason">Услышали: «{proposal.heard}»</p> : null}
              <button type="button" className="btn-ghost" onClick={() => setProposal(null)}>Закрыть</button>
            </>
          )}
        </div>
      ) : null}
    </div>
  );
}

function ShiftSheet({
  dish,
  dishes,
  onClose,
  onPortions,
  onStop,
  onResume,
  onPriority,
}: {
  dish: StopEntry;
  dishes: StopEntry[];
  onClose: () => void;
  onPortions: (delta: number) => void;
  onStop: (reason: StopReason, note: string) => void;
  onResume: () => void;
  onPriority: () => void;
}) {
  const { client } = useSession();
  const [note, setNote] = useState("");
  const history = useQuery({
    queryKey: ["shift-history", dish.dishId],
    queryFn: () => client.shiftHistory(dish.dishId),
  });
  const events = (history.data ?? []).slice(0, 8);
  const why = dish.isStopped ? stopWhy(dish) : null;
  const stockStop = dish.isStopped && dish.stopSource === "Stock";
  const canResume = dish.isStopped && !stockStop && dish.hasLimit && dish.portionsPossible > 0;
  const qty = dish.isStopped ? "0" : dish.hasLimit ? String(dish.portionsPossible) : "∞";
  return (
    <div className="modal-back" onClick={onClose}>
      <div className="shift-sheet" role="dialog" aria-labelledby="shift-dish" onClick={(event) => event.stopPropagation()}>
        <div>
          <p className="chip-label">Плитка смены</p>
          <h2 id="shift-dish">{dish.dishName}</h2>
          <p className="reason">
            {dish.isStopped ? "Стоп — зал не берёт." : dish.hasLimit ? `${dish.portionsPossible} порц. на заготовке.` : "Лимит заготовки ещё не задан."}
            {dish.isPriority && !dish.isStopped ? " Приоритет для зала." : ""}
          </p>
          {why ? (
            <p className="reason">
              {why.source}{why.category ? ` · ${why.category}` : ""}. {why.detail}
            </p>
          ) : null}
          {dish.isCandidate && !dish.isStopped ? (
            <p className="reason">Кандидат системы: {dish.candidateReason}. Зал ещё открыт, пока не поставишь стоп.</p>
          ) : null}
        </div>
        <div className="shift-stepper">
          <button type="button" className="btn-ghost" onClick={() => onPortions(-1)} aria-label="Минус порция">−</button>
          <strong>{qty}</strong>
          <button type="button" className="btn-ghost" onClick={() => onPortions(1)} aria-label="Плюс порция">+</button>
        </div>
        <div className="shift-jumps">
          <button type="button" className="btn-ghost" onClick={() => onPortions(3)}>+3</button>
          <button type="button" className="btn-ghost" onClick={() => onPortions(5)}>+5</button>
          <button type="button" className="btn-ghost" onClick={() => onPortions(-3)}>−3</button>
          <button type="button" className="btn-ghost" onClick={() => onPortions(-5)}>−5</button>
        </div>
        <p className="reason">+3 / +5 после доготовки противня. Это не приход на склад. 0 порций = стоп.</p>
        {/* Голос на плитке. Замени false на true, когда нужен для демо. */}
        {false ? <PortionVoice dish={dish} dishes={dishes} onConfirm={onPortions} /> : null}
        <button
          type="button"
          className={dish.isPriority ? "btn-ghost" : "btn"}
          disabled={dish.isStopped}
          onClick={onPriority}
        >
          {dish.isPriority ? "Убрать из приоритета" : "В приоритет — предлагать залу"}
        </button>
        {dish.isStopped ? (
          <>
            {stockStop ? (
              <p className="reason">Стоп склада. + порций зал не откроет — блюдо вернётся само, когда сырья хватит на порцию.</p>
            ) : (
              <button type="button" className="btn" disabled={!canResume} onClick={onResume}>
                {canResume ? "Снять стоп" : "Сначала + порций, потом снять стоп"}
              </button>
            )}
          </>
        ) : (
          <div>
            <p className="chip-label">Поставить в стоп</p>
            <textarea
              className="shift-note"
              value={note}
              onChange={(event) => setNote(event.target.value)}
              placeholder="Заметка для зала: почему закрыли"
              maxLength={300}
              rows={3}
            />
            <div className="shift-reasons">
              {STOP_REASONS.map((reason) => (
                <button key={reason.id} type="button" className="btn-ghost" onClick={() => onStop(reason.id, note.trim())}>
                  {reason.label}
                </button>
              ))}
            </div>
          </div>
        )}
        <div>
          <p className="chip-label">История</p>
          {events.length === 0 ? <p className="reason">Пока пусто.</p> : (
            <div className="shift-history">
              {events.map((event: ShiftHistoryEntry) => (
                <article key={event.id}>
                  <strong>{event.text}</strong>
                  <p className="reason">{event.actorLogin} · {formatWhen(event.at)}</p>
                </article>
              ))}
            </div>
          )}
        </div>
        <button type="button" className="text-btn" onClick={onClose}>Закрыть</button>
      </div>
    </div>
  );
}

function WasteSheet({
  onClose,
  onDone,
}: {
  onClose: () => void;
  onDone: (reason: WriteOffReason, items: { productId: string; quantity: number }[]) => Promise<void>;
}) {
  const { client } = useSession();
  const products = useQuery({ queryKey: ["products"], queryFn: () => client.products() });
  const [reason, setReason] = useState<WriteOffReason | null>(null);
  const [qty, setQty] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const active = (products.data ?? []).filter((product: Product) => product.isActive);
  const picked = active
    .map((product) => ({ productId: product.id, quantity: Number(qty[product.id] ?? "") }))
    .filter((item) => item.quantity > 0);

  async function submit() {
    if (!reason || picked.length === 0) return;
    setPending(true);
    setError(null);
    try {
      await onDone(reason, picked);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Акт не проведён");
    } finally {
      setPending(false);
    }
  }

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="shift-sheet" role="dialog" aria-labelledby="waste-title" onClick={(event) => event.stopPropagation()}>
        <div>
          <p className="chip-label">Акт списания</p>
          <h2 id="waste-title">{reason ? "Что списать" : "Сначала причина"}</h2>
          <p className="reason">Количество в единицах склада. Без партии спишется по FEFO.</p>
        </div>
        {!reason ? (
          <div className="shift-reasons">
            {WASTE_REASONS.map((item) => (
              <button key={item.id} type="button" className="btn-ghost" onClick={() => setReason(item.id)}>
                {item.label}
              </button>
            ))}
          </div>
        ) : (
          <div className="stack">
            {active.map((product) => (
              <label key={product.id} className="waste-line">
                <strong>{product.name}</strong>
                <input
                  inputMode="decimal"
                  value={qty[product.id] ?? ""}
                  placeholder="0"
                  onChange={(event) => setQty((current) => ({ ...current, [product.id]: event.target.value }))}
                />
              </label>
            ))}
            {error ? <p className="error-text">{error}</p> : null}
            <button type="button" className="btn" disabled={picked.length === 0 || pending} onClick={() => void submit()}>
              Списать
            </button>
            <button type="button" className="text-btn" onClick={() => setReason(null)}>Другая причина</button>
          </div>
        )}
        <button type="button" className="text-btn" onClick={onClose}>Закрыть</button>
      </div>
    </div>
  );
}
