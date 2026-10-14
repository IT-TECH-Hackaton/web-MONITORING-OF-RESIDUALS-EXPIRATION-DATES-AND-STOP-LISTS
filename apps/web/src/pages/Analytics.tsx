import type { SpendReport } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Bar, BarChart, Cell, Pie, PieChart, XAxis, YAxis } from "recharts";
import { ChartContainer, ChartTooltip } from "@/components/ui/chart";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, Qty } from "../ui";
import { WeeklyForecastPanel } from "./WeeklyForecast";

const periods = [
  { days: 7, label: "7 дней" },
  { days: 30, label: "30 дней" },
  { days: 0, label: "Всё время" },
] as const;

const roles: Record<string, string> = { Manager: "менеджер", Kitchen: "кухня", Waiter: "официант" };
const wasteLabels: Record<string, string> = {
  Expiry: "Срок",
  Waste: "Брак",
  Spoilage: "Порча",
  Training: "Проработка",
  StaffMeal: "Питание",
};
const sliceColors = ["var(--accent)", "var(--amber)", "var(--sage)", "var(--chart-4)", "var(--chart-5)", "var(--ink-soft)"];
const dayColors = ["#ef5340", "#e0a14a", "#5dcaa0", "#6cb6ff", "#c084fc", "#f07171", "#f5c16c", "#48c9b0", "#85c1e9", "#f0b27a"];

function rub(value: number) {
  return new Intl.NumberFormat("ru-RU", { style: "currency", currency: "RUB", maximumFractionDigits: 0 }).format(value);
}

function lasted(openedAt: string, closedAt: string | null) {
  const start = new Date(openedAt).getTime();
  const end = closedAt ? new Date(closedAt).getTime() : Date.now();
  const minutes = Math.max(0, Math.round((end - start) / 60000));
  if (minutes < 60) return `${minutes} мин`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} ч` : `${hours} ч ${rest} мин`;
}

function dayLabel(day: string) {
  return new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "short" }).format(new Date(`${day}T00:00:00`));
}

function slicesOf(rows: { key: string; name: string; rub: number }[]) {
  const total = rows.reduce((sum, row) => sum + row.rub, 0);
  const top = rows.slice(0, 5);
  const rest = total - top.reduce((sum, row) => sum + row.rub, 0);
  const parts = top.map((row) => ({ ...row }));
  if (rest > 0.5) parts.push({ key: "rest", name: "Остальное", rub: rest });
  return parts.map((part, index) => ({ ...part, fill: sliceColors[index] ?? sliceColors.at(-1)!, pct: total <= 0 ? 0 : (part.rub / total) * 100 }));
}

function Tip({
  active,
  payload,
}: {
  active?: boolean;
  payload?: ReadonlyArray<{ value?: unknown; payload?: { name?: string; label?: string; meta?: string; pct?: number; fill?: string; deliveries?: number } }>;
}) {
  if (!active || !payload?.length) return null;
  const row = payload[0].payload;
  const value = Number(payload[0].value ?? 0);
  return (
    <div className="rep-tip">
      <i style={{ background: row?.fill ?? "var(--accent)" }} />
      <div>
        <strong>{row?.name ?? row?.label}</strong>
        {row?.meta ? <p>{row.meta}</p> : null}
        {row?.deliveries != null ? <p>поставок {row.deliveries}</p> : null}
      </div>
      <em>
        {rub(value)}
        {row?.pct != null ? <span>{Math.round(row.pct)}%</span> : null}
      </em>
    </div>
  );
}

function Ring({ title, center, rows }: { title: string; center: string; rows: { key: string; name: string; rub: number }[] }) {
  const slices = slicesOf(rows);
  const total = rows.reduce((sum, row) => sum + row.rub, 0);
  if (slices.length === 0) {
    return (
      <article className="rep-fold">
        <h2 className="rep-block-title">{title}</h2>
        <div className="rep-fold-body"><Empty>За этот период пусто.</Empty></div>
      </article>
    );
  }
  return (
    <article className="rep-fold">
      <h2 className="rep-block-title">{title}</h2>
      <div className="rep-fold-body">
        <div className="rep-wheel-wrap">
          <div className="rep-donut">
            <ChartContainer config={{ rub: { label: "Сумма" } }} initialDimension={{ width: 188, height: 188 }} className="aspect-square h-[188px] w-[188px]">
              <PieChart>
                <ChartTooltip content={<Tip />} />
                <Pie data={slices} dataKey="rub" nameKey="name" innerRadius={58} outerRadius={86} paddingAngle={2} stroke="var(--card)" animationDuration={700}>
                  {slices.map((slice) => <Cell key={slice.key} fill={slice.fill} />)}
                </Pie>
              </PieChart>
            </ChartContainer>
            <div className="rep-wheel-hole">
              <span>{center}</span>
              <strong>{rub(total)}</strong>
            </div>
          </div>
          <ul className="rep-legend">
            {slices.map((slice) => (
              <li key={slice.key}>
                <i style={{ background: slice.fill }} />
                <span>{slice.name}</span>
                <strong>{rub(slice.rub)}</strong>
                <em>{Math.round(slice.pct)}%</em>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </article>
  );
}

function People({ rows }: { rows: SpendReport["people"] }) {
  if (rows.length === 0) return <Empty>В этом периоде поставок не было.</Empty>;
  const height = rows.length * 44;
  return (
    <ChartContainer config={{ rub: { label: "Закупка", color: "var(--amber)" } }} initialDimension={{ width: 480, height }} className="aspect-auto w-full" style={{ height }}>
      <BarChart data={rows.map((row) => ({ ...row, label: row.name, meta: `${roles[row.role] ?? row.role} · поставок ${row.deliveries}`, fill: "var(--amber)" }))} layout="vertical" margin={{ top: 4, right: 88, left: 8, bottom: 4 }}>
        <XAxis type="number" hide />
        <YAxis type="category" dataKey="label" width={96} tickLine={false} axisLine={false} />
        <ChartTooltip cursor={{ fill: "var(--paper)" }} content={<Tip />} />
        <Bar dataKey="rub" fill="var(--amber)" radius={[0, 8, 8, 0]} barSize={12} animationDuration={700} />
      </BarChart>
    </ChartContainer>
  );
}

export function AnalyticsPage() {
  const { client } = useSession();
  const [days, setDays] = useState<(typeof periods)[number]["days"]>(30);
  const spend = useQuery({ queryKey: ["spend", days], queryFn: () => client.spend(days) });
  const deficits = useQuery({ queryKey: ["deficits"], queryFn: () => client.deficits() });
  const weekly = useQuery({ queryKey: ["weekly-forecast"], queryFn: () => client.weeklyForecast() });
  const shifts = useQuery({ queryKey: ["shift-journal"], queryFn: () => client.shiftJournal() });
  const data = spend.data;

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Аналитика</h1>
          <p>Когда потратили, хватит ли на неделю и куда утекло мимо тарелки.</p>
        </div>
        <div className="shift-filters" role="tablist" aria-label="Период закупок">
          {periods.map((item) => (
            <button key={item.days} type="button" className={days === item.days ? "filter-chip is-on" : "filter-chip"} onClick={() => setDays(item.days)}>
              {item.label}
            </button>
          ))}
        </div>
      </header>

      <article className="rep-fold">
        <h2 className="rep-block-title">Смены</h2>
        <div className="rep-fold-body">
          {shifts.isLoading ? <p>Смотрим смены…</p> : null}
          {shifts.error ? <ErrorText error={shifts.error} /> : null}
          {shifts.data?.length === 0 ? <Empty>Смен ещё не было.</Empty> : null}
          {shifts.data && shifts.data.length > 0 ? (
            <div className="rep-scroll">
              <table className="rep-table rep-staff">
                <thead>
                  <tr><th>Статус</th><th>Открыл</th><th>Когда</th><th>Закрыл</th><th>Когда</th><th className="num">Длилась</th></tr>
                </thead>
                <tbody>
                  {shifts.data.map((row) => (
                    <tr key={row.id}>
                      <td>{row.isOpen ? "открыта" : "закрыта"}</td>
                      <td>{row.openedBy}</td>
                      <td>{formatWhen(row.openedAt)}</td>
                      <td className={row.closedBy ? undefined : "is-zero"}>{row.closedBy ?? "—"}</td>
                      <td className={row.closedAt ? undefined : "is-zero"}>{row.closedAt ? formatWhen(row.closedAt) : "—"}</td>
                      <td className="num">{lasted(row.openedAt, row.closedAt)}{row.isOpen ? " · идёт" : ""}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </div>
      </article>

      {spend.isLoading ? <p>Считаем период…</p> : null}
      {spend.error ? <ErrorText error={spend.error} /> : null}
      {data ? (
        <>
          <section className="rep-kpis">
            <article className="is-rose"><span>Потрачено</span><strong>{rub(data.totalRub)}</strong></article>
            <article><span>Дней с поставкой</span><strong>{data.days.filter((row) => row.rub > 0).length}</strong></article>
            <article className="is-amber"><span>Списано</span><strong>{rub(data.waste.reduce((sum, row) => sum + row.rub, 0))}</strong></article>
          </section>

          <article className="rep-fold">
            <h2 className="rep-block-title">Закупки по дням</h2>
            <div className="rep-fold-body">
              {data.days.length === 0 ? <Empty>В этом периоде поставок не было.</Empty> : (
                <ChartContainer config={{ rub: { label: "Сумма", color: "var(--accent)" } }} initialDimension={{ width: 720, height: 260 }} className="aspect-auto w-full" style={{ height: 260 }}>
                  <BarChart data={data.days.map((row, index) => ({ label: dayLabel(row.day), rub: row.rub, deliveries: row.deliveries, fill: row.rub > 0 ? dayColors[index % dayColors.length] : "var(--paper)" }))} margin={{ top: 8, right: 4, left: 0, bottom: 4 }} barCategoryGap="18%">
                    <XAxis dataKey="label" tickLine={false} axisLine={false} interval={data.days.length > 16 ? 1 : 0} tick={{ fontSize: 11 }} angle={data.days.length > 16 ? -40 : 0} textAnchor={data.days.length > 16 ? "end" : "middle"} height={data.days.length > 16 ? 58 : 30} />
                    <YAxis hide />
                    <ChartTooltip cursor={{ fill: "var(--paper)" }} content={<Tip />} />
                    <Bar dataKey="rub" radius={[5, 5, 0, 0]} maxBarSize={22} animationDuration={700} background={{ fill: "var(--paper)", radius: 5 }}>
                      {data.days.map((row, index) => (
                        <Cell key={row.day} fill={row.rub > 0 ? dayColors[index % dayColors.length] : "transparent"} />
                      ))}
                    </Bar>
                  </BarChart>
                </ChartContainer>
              )}
            </div>
          </article>

          <section className="rep-grid">
            <Ring
              title="Куда ушли деньги"
              center="Закупка"
              rows={data.products.map((row) => ({ key: row.productId, name: row.productName, rub: row.rub }))}
            />
            <article className="rep-fold">
              <h2 className="rep-block-title">Кто потратил</h2>
              <div className="rep-fold-body"><People rows={data.people} /></div>
            </article>
          </section>

          <section className="rep-grid">
            <Ring
              title="Списания"
              center="Списано"
              rows={data.waste.map((row) => ({ key: row.reason, name: wasteLabels[row.reason] ?? row.reason, rub: row.rub }))}
            />
            <article className="rep-fold">
              <h2 className="rep-block-title">Дефицит</h2>
              <div className="rep-fold-body">
                {deficits.isLoading ? <p>Загрузка…</p> : null}
                {deficits.error ? <ErrorText error={deficits.error} /> : null}
                {deficits.data?.length === 0 ? <Empty>Дефицитных продуктов нет.</Empty> : null}
                {deficits.data && deficits.data.length > 0 ? (
                  <div className="rep-scroll">
                    <table className="rep-table">
                      <thead><tr><th>Продукт</th><th className="num">Отказов</th><th className="num">Не хватило</th><th className="num">Сейчас</th></tr></thead>
                      <tbody>
                        {deficits.data.map((row) => (
                          <tr key={row.productId}>
                            <td>{row.productName}</td>
                            <td className={row.rejectionCount === 0 ? "num is-zero" : "num"}>{row.rejectionCount}</td>
                            <td className="num"><Qty value={row.totalDeficit} unit={row.unit} /></td>
                            <td className="num"><Qty value={row.available} unit={row.unit} /></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : null}
              </div>
            </article>
          </section>
        </>
      ) : null}

      {weekly.error ? <ErrorText error={weekly.error} /> : null}
      {weekly.data ? (
        <>
          <WeeklyForecastPanel report={weekly.data} shortagesOnly />
          <details className="rep-fold">
            <summary className="rep-fold-head">
              <span>Все продукты прогноза</span>
              <em>{weekly.data.items.length}</em>
            </summary>
            <div className="rep-fold-body">
              <WeeklyForecastPanel report={weekly.data} plain />
            </div>
          </details>
        </>
      ) : null}
    </div>
  );
}
