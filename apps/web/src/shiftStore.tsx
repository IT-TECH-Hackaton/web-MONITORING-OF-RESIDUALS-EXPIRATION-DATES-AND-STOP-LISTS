import { newId } from "@repo/contracts";
import { useEffect, useState } from "react";
import {
  demoHistory,
  demoShiftDishes,
  demoUseFirst,
  reasonLabel,
  withPortions,
  type ShiftBatch,
  type ShiftDish,
  type ShiftEvent,
  type StopReason,
} from "./shiftDemo";

const storageKey = "monitoring-shift-demo";

export type ShiftState = {
  dishes: ShiftDish[];
  batches: ShiftBatch[];
  history: ShiftEvent[];
  lastHint: string | null;
};

function seed(): ShiftState {
  return {
    dishes: demoShiftDishes.map((dish) => ({ ...dish })),
    batches: demoUseFirst.map((batch) => ({ ...batch })),
    history: demoHistory(),
    lastHint: null,
  };
}

function read(): ShiftState {
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return seed();
    const parsed = JSON.parse(raw) as ShiftState;
    if (!Array.isArray(parsed.dishes) || parsed.dishes.length === 0) return seed();
    const have = new Set(parsed.dishes.map((dish) => dish.dishId));
    const extra = demoShiftDishes.filter((dish) => !have.has(dish.dishId));
    return {
      ...seed(),
      ...parsed,
      dishes: [...parsed.dishes, ...extra],
      lastHint: parsed.lastHint ?? null,
    };
  } catch {
    return seed();
  }
}

let memory = read();
const listeners = new Set<(state: ShiftState) => void>();

function emit(next: ShiftState) {
  memory = next;
  localStorage.setItem(storageKey, JSON.stringify({ ...next, lastHint: next.lastHint }));
  listeners.forEach((listener) => listener(next));
}

function log(dishId: string, actor: string, text: string): ShiftEvent {
  return { id: newId(), dishId, at: new Date().toISOString(), actor, text };
}

function patchDish(dishId: string, actor: string, update: (dish: ShiftDish) => ShiftDish, text: string) {
  const current = memory.dishes.find((dish) => dish.dishId === dishId);
  if (!current) return;
  const nextDish = update(current);
  emit({
    ...memory,
    dishes: memory.dishes.map((dish) => (dish.dishId === dishId ? nextDish : dish)),
    history: [log(dishId, actor, text), ...memory.history],
    lastHint: `${nextDish.dishName}: ${text}`,
  });
}

export const shiftActions = {
  changePortions(dishId: string, delta: number, actor: string) {
    const dish = memory.dishes.find((item) => item.dishId === dishId);
    if (!dish || delta === 0) return;
    const next = dish.portionsPossible + delta;
    const verb = delta > 0 ? `+${delta} порц.` : `−${Math.abs(delta)} порц.`;
    patchDish(dishId, actor, (current) => withPortions(current, current.portionsPossible + delta), next <= 0 ? `${verb} → стоп` : verb);
  },
  stop(dishId: string, reason: StopReason, actor: string) {
    const label = reasonLabel(reason) ?? "стоп";
    patchDish(dishId, actor, (current) => ({
      ...current,
      isStopped: true,
      portionsPossible: 0,
      isPriority: false,
      stopReason: reason,
      reason: label,
    }), `Стоп: ${label.toLocaleLowerCase("ru")}`);
  },
  resume(dishId: string, actor: string) {
    const dish = memory.dishes.find((item) => item.dishId === dishId);
    if (!dish || dish.portionsPossible <= 0) return;
    patchDish(dishId, actor, (current) => ({
      ...current,
      isStopped: false,
      stopReason: null,
      reason: current.portionsPossible <= 5 ? "Мало заготовки" : current.candidateReason,
    }), "Сняли стоп");
  },
  setPriority(dishId: string, on: boolean, actor: string) {
    const dish = memory.dishes.find((item) => item.dishId === dishId);
    if (!dish || dish.isStopped) return;
    patchDish(dishId, actor, (current) => ({ ...current, isPriority: on }), on ? "В приоритет — предлагать залу" : "Убрали из приоритета");
  },
  toggleOpened(batchId: string) {
    emit({
      ...memory,
      batches: memory.batches.map((batch) =>
        batch.batchId === batchId ? { ...batch, opened: !batch.opened } : batch,
      ),
    });
  },
  applyOrder(items: { dishId: string; portions: number }[], actor: string) {
    let dishes = memory.dishes;
    const events: ShiftEvent[] = [];
    const names: string[] = [];
    for (const item of items) {
      const dish = dishes.find((candidate) => candidate.dishId === item.dishId);
      if (!dish || dish.isStopped || item.portions <= 0) continue;
      const next = withPortions(dish, dish.portionsPossible - item.portions);
      dishes = dishes.map((candidate) => (candidate.dishId === dish.dishId ? next : candidate));
      const text = next.isStopped
        ? `Зал −${item.portions} → стоп`
        : `Зал −${item.portions}, осталось ${next.portionsPossible}`;
      events.push(log(dish.dishId, actor, text));
      names.push(`${dish.dishName} −${item.portions}`);
    }
    if (events.length === 0) return;
    emit({
      ...memory,
      dishes,
      history: [...events, ...memory.history],
      lastHint: names.join(", "),
    });
  },
  noteWaste(text: string) {
    emit({ ...memory, lastHint: text });
  },
};

if (typeof window !== "undefined") {
  window.addEventListener("storage", (event) => {
    if (event.key !== storageKey || !event.newValue) return;
    try {
      memory = JSON.parse(event.newValue) as ShiftState;
      listeners.forEach((listener) => listener(memory));
    } catch {
      /* ignore */
    }
  });
}

export function useShift() {
  const [state, setState] = useState(memory);
  useEffect(() => {
    listeners.add(setState);
    setState(memory);
    return () => {
      listeners.delete(setState);
    };
  }, []);
  return { ...state, ...shiftActions };
}
