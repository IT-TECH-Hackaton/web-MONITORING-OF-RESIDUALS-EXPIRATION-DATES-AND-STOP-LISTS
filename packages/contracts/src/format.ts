import type { Freshness, Unit } from "./types";

export function newId(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }

  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (char) => {
    const random = Math.floor(Math.random() * 16);
    const value = char === "x" ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}

export function unitLabel(unit: Unit): string {
  if (unit === "G") return "г";
  if (unit === "Ml") return "мл";
  return "шт";
}

export function formatQty(value: number, unit: Unit): string {
  const amount = new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 3 }).format(value);
  return `${amount} ${unitLabel(unit)}`;
}

export function formatHours(hours: number): string {
  if (hours < 0) return "срок вышел";
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} мин`;
  if (hours < 48) return `${hours.toFixed(1)} ч`;
  return `${Math.round(hours / 24)} дн`;
}

export function freshnessLabel(freshness: Freshness): string {
  if (freshness === "Ok") return "В норме";
  if (freshness === "Expiring") return "Скоро срок";
  if (freshness === "Expired") return "Просрочено";
  return "Нет остатка";
}
