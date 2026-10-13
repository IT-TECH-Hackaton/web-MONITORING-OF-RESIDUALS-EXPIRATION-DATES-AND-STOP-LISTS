import { formatHours, formatQty, freshnessLabel, type Freshness, type StopEntry, type Unit } from "@repo/contracts";
import type { ReactNode } from "react";

export const LOW_PORTIONS = 5;

export function isLow(entry: StopEntry) {
  if (entry.isLow != null) return entry.isLow;
  return !entry.isStopped && entry.hasLimit !== false && entry.portionsPossible > 0 && entry.portionsPossible <= LOW_PORTIONS;
}

export function StatusPill({ entry }: { entry: StopEntry }) {
  if (entry.isStopped) return <span className="pill pill-stop">стоп</span>;
  if (isLow(entry)) return <span className="pill pill-low">мало</span>;
  return <span className="pill pill-ok">ок</span>;
}

export function DishTile({
  entry,
  priority,
  candidate,
  showReason,
  onClick,
}: {
  entry: StopEntry;
  priority?: boolean;
  candidate?: boolean;
  showReason?: boolean;
  onClick?: () => void;
}) {
  const classes = [
    "tile",
    entry.isStopped ? "tile-stop" : isLow(entry) ? "tile-low" : "tile-ok",
    priority ? "tile-priority" : "",
    candidate && !entry.isStopped ? "tile-candidate" : "",
    onClick ? "tile-tap" : "",
  ].filter(Boolean).join(" ");
  const inner = (
    <>
      <span className="qty">{entry.isStopped ? "0" : entry.hasLimit === false ? "∞" : entry.portionsPossible}</span>
      <strong>{entry.dishName}</strong>
      {showReason && (entry.reason || entry.candidateReason) ? <p className="reason">{entry.reason ?? entry.candidateReason}</p> : null}
      <div className="btn-row">
        <StatusPill entry={entry} />
        {candidate && !entry.isStopped ? <span className="pill pill-low">кандидат</span> : null}
        {priority ? <span className="pill pill-priority">приоритет</span> : null}
      </div>
    </>
  );
  if (onClick) {
    return <button type="button" className={classes} onClick={onClick}>{inner}</button>;
  }
  return <article className={classes}>{inner}</article>;
}

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
