import { formatHours, formatQty, freshnessLabel, type Freshness, type Unit } from "@repo/contracts";
import type { ReactNode } from "react";

export function formatWhen(iso: string) {
  return new Intl.DateTimeFormat("ru-RU", {
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(iso));
}

export function Qty({ value, unit }: { value: number; unit: Unit }) {
  return <>{formatQty(value, unit)}</>;
}

export function Hours({ value }: { value: number | null }) {
  if (value === null) return <>—</>;
  return <>{formatHours(value)}</>;
}

export function FreshnessPill({ freshness }: { freshness: Freshness }) {
  return <span className={`pill pill-${freshness.toLowerCase()}`}>{freshnessLabel(freshness)}</span>;
}

export function Panel({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="panel">
      <header className="panel-head">
        <h2>{title}</h2>
        {action}
      </header>
      {children}
    </section>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}

export function ErrorText({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : "Не удалось загрузить данные";
  return <p className="error-text">{message}</p>;
}
