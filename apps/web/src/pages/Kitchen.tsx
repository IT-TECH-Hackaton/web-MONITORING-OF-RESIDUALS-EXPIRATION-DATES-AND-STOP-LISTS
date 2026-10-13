import { formatHours, formatQty } from "@repo/contracts";
import { useMemo, useState } from "react";
import { useSession } from "../session";
import {
  demoHotSuggest,
  demoWasteProducts,
  STOP_REASONS,
  WASTE_REASONS,
  type ShiftDish,
  type StopReason,
} from "../shiftDemo";
import { useShift } from "../shiftStore";
import { DishTile, Empty, formatWhen, isLow, Panel } from "../ui";

type Filter = "all" | "stop" | "low" | "candidate" | "priority";

const FILTERS: { id: Filter; label: string }[] = [
  { id: "all", label: "Все" },
  { id: "stop", label: "Стоп" },
  { id: "low", label: "Мало" },
  { id: "candidate", label: "Кандидаты" },
  { id: "priority", label: "Приоритет" },
];

function sortShift(dishes: ShiftDish[]) {
  return [...dishes].sort((a, b) => {
    const rank = (dish: ShiftDish) => {
      if (dish.isStopped) return 0;
      if (dish.isCandidate) return 1;
      if (isLow(dish)) return 2;
      if (dish.isPriority) return 3;
      return 4;
    };
    return rank(a) - rank(b) || a.dishName.localeCompare(b.dishName, "ru");
  });
}

function matchesFilter(dish: ShiftDish, filter: Filter) {
  if (filter === "stop") return dish.isStopped;
  if (filter === "low") return isLow(dish);
  if (filter === "candidate") return dish.isCandidate && !dish.isStopped;
  if (filter === "priority") return dish.isPriority && !dish.isStopped;
  return true;
}

export function KitchenPage() {
  const { session } = useSession();
  const actor = session?.login ?? "шеф";
  const shift = useShift();
  const [openId, setOpenId] = useState<string | null>(null);
  const [wasteOpen, setWasteOpen] = useState(false);
  const [filter, setFilter] = useState<Filter>("all");
  const [query, setQuery] = useState("");
  const open = shift.dishes.find((dish) => dish.dishId === openId) ?? null;
  const needle = query.trim().toLocaleLowerCase("ru");

  const counts = useMemo(() => ({
    all: shift.dishes.length,
    stop: shift.dishes.filter((dish) => dish.isStopped).length,
    low: shift.dishes.filter(isLow).length,
    candidate: shift.dishes.filter((dish) => dish.isCandidate && !dish.isStopped).length,
    priority: shift.dishes.filter((dish) => dish.isPriority && !dish.isStopped).length,
  }), [shift.dishes]);

  const visible = useMemo(() => {
    const found = shift.dishes.filter((dish) => {
      if (!matchesFilter(dish, filter)) return false;
      if (!needle) return true;
      return dish.dishName.toLocaleLowerCase("ru").includes(needle);
    });
    return sortShift(found);
  }, [shift.dishes, filter, needle]);

  const hot = demoHotSuggest
    .map((item) => {
      const dish = shift.dishes.find((entry) => entry.dishId === item.dishId);
      if (!dish || dish.isStopped || dish.isPriority) return null;
      return { dish, reason: item.reason };
    })
    .filter((item): item is { dish: ShiftDish; reason: string } => item != null)
    .slice(0, 4);

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Смена</h1>
          <p>Стоп и порции заготовки — рука шефа. Кандидат системы зал не закрывает. Заказ с зала сам минусует плитку.</p>
        </div>
        <button type="button" className="btn-ghost" onClick={() => setWasteOpen(true)}>Акт списания</button>
      </div>
      {shift.lastHint ? <p className="hint">{shift.lastHint}</p> : null}
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
                <button type="button" className="btn" onClick={() => shift.setPriority(item.dish.dishId, true, actor)}>
                  В приоритет
                </button>
              </div>
            ))}
          </div>
        </Panel>
      ) : null}
      {visible.length === 0 ? <Empty>Нет блюд в этом фильтре.</Empty> : (
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
        {shift.batches.length === 0 ? <Empty>Нет коротких и вскрытых партий.</Empty> : (
          <div className="stack">
            {shift.batches.map((batch) => (
              <button
                key={batch.batchId}
                type="button"
                className={batch.opened ? "batch-row is-open" : "batch-row"}
                onClick={() => shift.toggleOpened(batch.batchId)}
              >
                <span>
                  <strong>{batch.productName}</strong>
                  <p className="reason">
                    {batch.deliveryNumber} · {formatQty(batch.remaining, batch.unit)} · {formatHours(batch.hoursLeft)}
                  </p>
                </span>
                <span className={batch.opened ? "pill pill-low" : "pill"}>{batch.opened ? "вскрыта" : "вскрыть"}</span>
              </button>
            ))}
          </div>
        )}
      </Panel>
      {open ? (
        <ShiftSheet
          dish={open}
          events={shift.history.filter((event) => event.dishId === open.dishId).slice(0, 8)}
          onClose={() => setOpenId(null)}
          onPortions={(delta) => shift.changePortions(open.dishId, delta, actor)}
          onStop={(reason) => shift.stop(open.dishId, reason, actor)}
          onResume={() => shift.resume(open.dishId, actor)}
          onPriority={() => shift.setPriority(open.dishId, !open.isPriority, actor)}
        />
      ) : null}
      {wasteOpen ? (
        <WasteSheet
          onClose={() => setWasteOpen(false)}
          onDone={(text) => { setWasteOpen(false); shift.noteWaste(text); }}
        />
      ) : null}
    </>
  );
}

function ShiftSheet({
  dish,
  events,
  onClose,
  onPortions,
  onStop,
  onResume,
  onPriority,
}: {
  dish: ShiftDish;
  events: { id: string; at: string; actor: string; text: string }[];
  onClose: () => void;
  onPortions: (delta: number) => void;
  onStop: (reason: StopReason) => void;
  onResume: () => void;
  onPriority: () => void;
}) {
  const canResume = dish.isStopped && dish.portionsPossible > 0;
  return (
    <div className="modal-back" onClick={onClose}>
      <div className="shift-sheet" role="dialog" aria-labelledby="shift-dish" onClick={(event) => event.stopPropagation()}>
        <div>
          <p className="chip-label">Плитка смены</p>
          <h2 id="shift-dish">{dish.dishName}</h2>
          <p className="reason">
            {dish.isStopped ? "Стоп — зал не берёт." : `${dish.portionsPossible} порц. на заготовке.`}
            {dish.isPriority && !dish.isStopped ? " Приоритет для зала." : ""}
          </p>
          {dish.isCandidate && !dish.isStopped ? (
            <p className="reason">Кандидат системы: {dish.candidateReason}. Зал ещё открыт, пока не подтвердишь стоп.</p>
          ) : null}
        </div>
        <div className="shift-stepper">
          <button type="button" className="btn-ghost" onClick={() => onPortions(-1)} aria-label="Минус порция">−</button>
          <strong>{dish.isStopped ? 0 : dish.portionsPossible}</strong>
          <button type="button" className="btn-ghost" onClick={() => onPortions(1)} aria-label="Плюс порция">+</button>
        </div>
        <div className="shift-jumps">
          <button type="button" className="btn-ghost" onClick={() => onPortions(3)}>+3</button>
          <button type="button" className="btn-ghost" onClick={() => onPortions(5)}>+5</button>
          <button type="button" className="btn-ghost" onClick={() => onPortions(-3)}>−3</button>
          <button type="button" className="btn-ghost" onClick={() => onPortions(-5)}>−5</button>
        </div>
        <p className="reason">+3 / +5 после доготовки противня. Это не приход на склад. 0 порций = стоп.</p>
        <button
          type="button"
          className={dish.isPriority ? "btn-ghost" : "btn"}
          disabled={dish.isStopped}
          onClick={onPriority}
        >
          {dish.isPriority ? "Убрать из приоритета" : "В приоритет — предлагать залу"}
        </button>
        {dish.isStopped ? (
          <button type="button" className="btn" disabled={!canResume} onClick={onResume}>
            {canResume ? "Снять стоп" : "Сначала + порций, потом снять стоп"}
          </button>
        ) : (
          <div>
            <p className="chip-label">Поставить в стоп</p>
            <div className="shift-reasons">
              {STOP_REASONS.map((reason) => (
                <button key={reason.id} type="button" className="btn-ghost" onClick={() => onStop(reason.id)}>
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
              {events.map((event) => (
                <article key={event.id}>
                  <strong>{event.text}</strong>
                  <p className="reason">{event.actor} · {formatWhen(event.at)}</p>
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

function WasteSheet({ onClose, onDone }: { onClose: () => void; onDone: (text: string) => void }) {
  const [reason, setReason] = useState<(typeof WASTE_REASONS)[number]["id"] | null>(null);
  const [qty, setQty] = useState<Record<string, number>>({});
  const picked = Object.entries(qty).filter(([, value]) => value > 0);

  return (
    <div className="modal-back" onClick={onClose}>
      <div className="shift-sheet" role="dialog" aria-labelledby="waste-title" onClick={(event) => event.stopPropagation()}>
        <div>
          <p className="chip-label">Акт списания</p>
          <h2 id="waste-title">{reason ? "Что списать" : "Сначала причина"}</h2>
          <p className="reason">Заглушка: на склад не уйдёт, пока нет API.</p>
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
            {demoWasteProducts.map((product) => (
              <div key={product.productId} className="waste-line">
                <strong>{product.name}</strong>
                <div className="stepper">
                  <button type="button" onClick={() => setQty((current) => ({ ...current, [product.productId]: Math.max(0, (current[product.productId] ?? 0) - 1) }))}>−</button>
                  <span>{qty[product.productId] ?? 0}</span>
                  <button type="button" onClick={() => setQty((current) => ({ ...current, [product.productId]: (current[product.productId] ?? 0) + 1 }))}>+</button>
                </div>
              </div>
            ))}
            <button
              type="button"
              className="btn"
              disabled={picked.length === 0}
              onClick={() => onDone(`Списано (${WASTE_REASONS.find((item) => item.id === reason)?.label}): ${picked.length} поз. Без склада.`)}
            >
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
