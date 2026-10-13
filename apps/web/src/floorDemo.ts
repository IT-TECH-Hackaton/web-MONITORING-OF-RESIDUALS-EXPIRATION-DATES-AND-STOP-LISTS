import type { StopEntry } from "@repo/contracts";

/** Заглушки на экране зала, без API. */
export const demoStopped: StopEntry[] = [
  { dishId: "demo-stop-1", dishName: "Стейк рибай", portionsPossible: 0, isStopped: true, reason: "Закончилась мраморная" },
  { dishId: "demo-stop-2", dishName: "Устрицы", portionsPossible: 0, isStopped: true, reason: "Нет поставки" },
  { dishId: "demo-stop-3", dishName: "Тартар из тунца", portionsPossible: 0, isStopped: true, reason: "Рыба не прошла входной" },
  { dishId: "demo-stop-4", dishName: "Каре ягнёнка", portionsPossible: 0, isStopped: true, reason: "Мало на заготовке" },
  { dishId: "demo-stop-5", dishName: "Крем-брюле", portionsPossible: 0, isStopped: true, reason: "Печь на десертах" },
];

export const demoPriority: StopEntry[] = [
  { dishId: "demo-hot-1", dishName: "Салат Капрезе", portionsPossible: 12, isStopped: false, reason: null },
  { dishId: "demo-hot-2", dishName: "Паста с лососем", portionsPossible: 8, isStopped: false, reason: null },
  { dishId: "demo-hot-3", dishName: "Тирамису", portionsPossible: 6, isStopped: false, reason: null },
];
