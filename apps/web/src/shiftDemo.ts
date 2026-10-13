import type { StopEntry, Unit } from "@repo/contracts";

/** Заглушки смены шефа и зала, без API. */

export type StopReason = "OutOfProduct" | "Waste" | "Quality" | "WontMakeIt" | "Equipment";

export type ShiftDish = StopEntry & {
  isPriority: boolean;
  isCandidate: boolean;
  candidateReason: string | null;
  stopReason: StopReason | null;
};

export type ShiftBatch = {
  batchId: string;
  productName: string;
  unit: Unit;
  deliveryNumber: string;
  remaining: number;
  hoursLeft: number;
  opened: boolean;
};

export type WasteProduct = {
  productId: string;
  name: string;
  unit: Unit;
};

export const STOP_REASONS: { id: StopReason; label: string }[] = [
  { id: "OutOfProduct", label: "Закончился продукт" },
  { id: "Waste", label: "Брак" },
  { id: "Quality", label: "Качество" },
  { id: "WontMakeIt", label: "Не успеем" },
  { id: "Equipment", label: "Техника" },
];

export const WASTE_REASONS = [
  { id: "Expiry", label: "Срок" },
  { id: "Waste", label: "Брак" },
  { id: "Spoilage", label: "Порча" },
  { id: "Training", label: "Проработка" },
  { id: "StaffMeal", label: "Питание" },
] as const;

export const demoShiftDishes: ShiftDish[] = [
  { dishId: "demo-stop-1", dishName: "Стейк рибай", portionsPossible: 0, isStopped: true, reason: "Закончилась мраморная", isPriority: false, isCandidate: false, candidateReason: null, stopReason: "OutOfProduct" },
  { dishId: "demo-stop-2", dishName: "Устрицы", portionsPossible: 0, isStopped: true, reason: "Нет поставки", isPriority: false, isCandidate: false, candidateReason: null, stopReason: "OutOfProduct" },
  { dishId: "demo-stop-3", dishName: "Тартар из тунца", portionsPossible: 0, isStopped: true, reason: "Рыба не прошла входной", isPriority: false, isCandidate: false, candidateReason: null, stopReason: "Quality" },
  { dishId: "demo-stop-4", dishName: "Каре ягнёнка", portionsPossible: 0, isStopped: true, reason: "Мало на заготовке", isPriority: false, isCandidate: false, candidateReason: null, stopReason: "OutOfProduct" },
  { dishId: "demo-stop-5", dishName: "Крем-брюле", portionsPossible: 0, isStopped: true, reason: "Печь на десертах", isPriority: false, isCandidate: false, candidateReason: null, stopReason: "Equipment" },
  { dishId: "demo-hot-1", dishName: "Салат Капрезе", portionsPossible: 12, isStopped: false, reason: null, isPriority: true, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-hot-2", dishName: "Паста с лососем", portionsPossible: 4, isStopped: false, reason: "Мало соуса", isPriority: true, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-hot-3", dishName: "Тирамису", portionsPossible: 6, isStopped: false, reason: null, isPriority: true, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-low-1", dishName: "Борщ", portionsPossible: 3, isStopped: false, reason: "Мало заготовки", isPriority: false, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-low-2", dishName: "Куриный бульон", portionsPossible: 2, isStopped: false, reason: "Мало заготовки", isPriority: false, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-cand-1", dishName: "Том ям", portionsPossible: 9, isStopped: false, reason: "Креветки: не хватает по техкарте", isPriority: false, isCandidate: true, candidateReason: "Креветки: не хватает по техкарте", stopReason: null },
  { dishId: "demo-cand-2", dishName: "Утка конфи", portionsPossible: 7, isStopped: false, reason: "Партия истекает за 8 часов", isPriority: false, isCandidate: true, candidateReason: "Партия истекает за 8 часов", stopReason: null },
  { dishId: "demo-ok-1", dishName: "Цезарь", portionsPossible: 18, isStopped: false, reason: null, isPriority: false, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-ok-2", dishName: "Пицца маргарита", portionsPossible: 20, isStopped: false, reason: null, isPriority: false, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-ok-3", dishName: "Брускетта", portionsPossible: 15, isStopped: false, reason: null, isPriority: false, isCandidate: false, candidateReason: null, stopReason: null },
  { dishId: "demo-ok-4", dishName: "Чизкейк", portionsPossible: 11, isStopped: false, reason: null, isPriority: false, isCandidate: false, candidateReason: null, stopReason: null },
];

export const demoStopped = demoShiftDishes.filter((dish) => dish.isStopped);
export const demoPriority = demoShiftDishes.filter((dish) => dish.isPriority && !dish.isStopped);

export const demoUseFirst: ShiftBatch[] = [
  { batchId: "demo-batch-1", productName: "Лосось", unit: "G", deliveryNumber: "П-104", remaining: 800, hoursLeft: 6, opened: true },
  { batchId: "demo-batch-2", productName: "Сливки 33%", unit: "Ml", deliveryNumber: "П-104", remaining: 1200, hoursLeft: 10, opened: false },
  { batchId: "demo-batch-3", productName: "Моцарелла", unit: "G", deliveryNumber: "П-101", remaining: 400, hoursLeft: 4, opened: true },
  { batchId: "demo-batch-4", productName: "Базилик", unit: "G", deliveryNumber: "П-108", remaining: 80, hoursLeft: 8, opened: false },
];

export const demoWasteProducts: WasteProduct[] = [
  { productId: "demo-p-1", name: "Лосось", unit: "G" },
  { productId: "demo-p-2", name: "Сливки 33%", unit: "Ml" },
  { productId: "demo-p-3", name: "Моцарелла", unit: "G" },
  { productId: "demo-p-4", name: "Базилик", unit: "G" },
  { productId: "demo-p-5", name: "Яйца", unit: "Pcs" },
];

export type ShiftEvent = {
  id: string;
  dishId: string;
  at: string;
  actor: string;
  text: string;
};

/** Алгоритм «горячих»: короткий срок / вскрытая партия. Шеф подтверждает приоритет сам. */
export const demoHotSuggest: { dishId: string; reason: string }[] = [
  { dishId: "demo-hot-2", reason: "Лосось: вскрытая партия, 6 ч" },
  { dishId: "demo-hot-1", reason: "Моцарелла: партия 4 ч" },
  { dishId: "demo-ok-2", reason: "Моцарелла: партия 4 ч — крутить пиццу" },
  { dishId: "demo-ok-3", reason: "Базилик: 8 ч, взять первым" },
  { dishId: "demo-cand-2", reason: "Утка: партия истекает за 8 ч" },
];

function hoursAgo(hours: number) {
  return new Date(Date.now() - hours * 3600_000).toISOString();
}

export function demoHistory(): ShiftEvent[] {
  return [
    { id: "h1", dishId: "demo-stop-1", at: hoursAgo(7), actor: "су", text: "Стоп: закончился продукт — мраморная" },
    { id: "h2", dishId: "demo-stop-2", at: hoursAgo(6.5), actor: "шеф", text: "Стоп: нет поставки" },
    { id: "h3", dishId: "demo-stop-3", at: hoursAgo(5), actor: "шеф", text: "Стоп: качество — рыба не прошла входной" },
    { id: "h4", dishId: "demo-stop-5", at: hoursAgo(4), actor: "су", text: "Стоп: техника — печь на десертах" },
    { id: "h5", dishId: "demo-hot-1", at: hoursAgo(3), actor: "шеф", text: "В приоритет: моцарелла на исходе" },
    { id: "h6", dishId: "demo-hot-2", at: hoursAgo(3), actor: "шеф", text: "В приоритет: вскрытый лосось" },
    { id: "h7", dishId: "demo-low-1", at: hoursAgo(1), actor: "система", text: "Мало: осталось 3 порции" },
  ];
}

export function reasonLabel(id: StopReason | null) {
  return STOP_REASONS.find((item) => item.id === id)?.label ?? null;
}

export function withPortions(dish: ShiftDish, portions: number): ShiftDish {
  const next = Math.max(0, portions);
  if (next === 0) {
    return {
      ...dish,
      portionsPossible: 0,
      isStopped: true,
      isPriority: false,
      stopReason: dish.stopReason ?? "OutOfProduct",
      reason: dish.reason && dish.isStopped ? dish.reason : "Нет порций на заготовке",
    };
  }
  return {
    ...dish,
    portionsPossible: next,
    isStopped: false,
    stopReason: null,
    reason: next <= 5 ? "Мало заготовки" : dish.isCandidate ? dish.candidateReason : null,
  };
}
