import type { MoneyReport, Unit } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import {
  Banknote,
  ChevronDown,
  ChevronsDownUp,
  ChevronsUpDown,
  Download,
  Printer,
  Receipt,
  ShoppingCart,
  Trash2,
  Wallet,
} from "lucide-react";
import { motion } from "motion/react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Bar, BarChart, Cell, LabelList, Pie, PieChart, XAxis, YAxis } from "recharts";
import { ChartContainer, ChartTooltip } from "@/components/ui/chart";
import { useSession } from "../session";
import { Empty, ErrorText, Qty } from "../ui";

const roles: Record<string, string> = {
  Manager: "менеджер",
  Kitchen: "кухня",
  Waiter: "официант",
};

const sliceColors = [
  "var(--accent)",
  "var(--amber)",
  "var(--sage)",
  "var(--chart-4)",
  "var(--chart-5)",
  "var(--ink-soft)",
];

function rub(value: number) {
  return new Intl.NumberFormat("ru-RU", { style: "currency", currency: "RUB", maximumFractionDigits: 0 }).format(value);
}

function downloadCsv(data: MoneyReport) {
  const rows = [
    ["Раздел", "Показатель", "Значение"],
    ["Итог", "Закуплено", String(data.purchaseRub)],
    ["Итог", "Выручка", String(data.revenueRub)],
    ["Итог", "Себестоимость", String(data.costRub)],
    ["Итог", "Брак и срок", String(data.wasteRub)],
    ["Итог", "Чистыми", String(data.netRub)],
    [],
    ["Сотрудник", "Роль", "Закупка", "Поставок", "Продажи", "Заказов"],
    ...data.staff.map((row) => [row.name, roles[row.role] ?? row.role, String(row.purchaseRub), String(row.deliveries), String(row.salesRub), String(row.orders)]),
    [],
    ["Продукт", "Количество", "Единица", "Сумма"],
    ...data.purchases.map((row) => [row.productName, String(row.quantity), row.unit, String(row.rub)]),
    [],
    ["Блюдо", "Цена", "Порций", "Выручка", "Себестоимость", "Чистыми"],
    ...data.dishes.map((row) => [row.dishName, String(row.salePrice), String(row.portions), String(row.revenueRub), String(row.costRub), String(row.netRub)]),
  ];
  const csv = rows.map((row) => row.map((cell) => `"${String(cell).replaceAll("\"", "\"\"")}"`).join(";")).join("\n");
  const blob = new Blob([`\uFEFF${csv}`], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `otchet-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

type Slice = { key: string; name: string; rub: number; pct: number; fill: string };

function purchaseSlices(data: MoneyReport): Slice[] {
  const total = data.purchaseRub;
  const top = data.purchases.slice(0, 5);
  const topSum = top.reduce((sum, row) => sum + row.rub, 0);
  const parts = top.map((row) => ({ key: row.productId, name: row.productName, rub: row.rub }));
  const rest = total - topSum;
  if (rest > 0.5) parts.push({ key: "rest", name: "Остальное", rub: rest });
  return parts.map((part, index) => ({
    ...part,
    pct: total <= 0 ? 0 : (part.rub / total) * 100,
    fill: sliceColors[index] ?? sliceColors.at(-1)!,
  }));
}

function RubTip({
  active,
  payload,
  format = rub,
}: {
  active?: boolean;
  payload?: ReadonlyArray<{
    name?: unknown;
    value?: unknown;
    payload?: { name?: string; label?: string; meta?: string; pct?: number; fill?: string };
  }>;
  format?: (value: number) => string;
}) {
  if (!active || !payload?.length) return null;
  const point = payload[0];
  const row = point.payload;
  const title = row?.name ?? row?.label ?? (typeof point.name === "string" ? point.name : "");
  const value = typeof point.value === "number" ? point.value : Number(point.value ?? 0);
  return (
    <div className="rep-tip">
      <i style={{ background: row?.fill ?? "var(--accent)" }} />
      <div>
        <strong>{title}</strong>
        {row?.meta ? <p>{row.meta}</p> : null}
      </div>
      <em>
        {format(value)}
        {row?.pct != null ? <span>{Math.round(row.pct)}%</span> : null}
      </em>
    </div>
  );
}

function Wheel({ data }: { data: MoneyReport }) {
  const slices = purchaseSlices(data);
  const [active, setActive] = useState<number | undefined>(undefined);
  if (slices.length === 0) {
    return (
      <div className="rep-wheel-wrap">
        <div className="rep-wheel-hole">
          <span>Закупка</span>
          <strong>{rub(0)}</strong>
        </div>
      </div>
    );
  }
  return (
    <div className="rep-wheel-wrap">
      <div className="rep-donut">
        <ChartContainer
          config={{ rub: { label: "Сумма" } }}
          initialDimension={{ width: 188, height: 188 }}
          className="aspect-square h-[188px] w-[188px]"
        >
          <PieChart>
            <ChartTooltip content={<RubTip />} />
            <Pie
              data={slices}
              dataKey="rub"
              nameKey="name"
              innerRadius={58}
              outerRadius={86}
              paddingAngle={2}
              stroke="var(--card)"
              animationDuration={700}
              onMouseEnter={(_, index) => setActive(index)}
              onMouseLeave={() => setActive(undefined)}
            >
              {slices.map((slice, index) => (
                <Cell key={slice.key} fill={slice.fill} opacity={active === undefined || active === index ? 1 : 0.45} />
              ))}
            </Pie>
          </PieChart>
        </ChartContainer>
        <div className="rep-wheel-hole">
          <span>Закупка</span>
          <strong>{rub(data.purchaseRub)}</strong>
        </div>
      </div>
      <ul className="rep-legend">
        {slices.map((slice, index) => (
          <li
            key={slice.key}
            className={active === index ? "is-active" : undefined}
            onMouseEnter={() => setActive(index)}
            onMouseLeave={() => setActive(undefined)}
          >
            <i style={{ background: slice.fill }} />
            <span>{slice.name}</span>
            <strong>{rub(slice.rub)}</strong>
            <em>{Math.round(slice.pct)}%</em>
          </li>
        ))}
      </ul>
    </div>
  );
}

function MoneyBars({
  rows,
  color,
  valueLabel,
  format = rub,
}: {
  rows: { key: string; label: string; value: number; meta?: string }[];
  color: string;
  valueLabel: string;
  format?: (value: number) => string;
}) {
  if (rows.length === 0) return <Empty>Пока нечего показать.</Empty>;
  const height = rows.length * 44;
  return (
    <ChartContainer
      config={{ value: { label: valueLabel, color } }}
      initialDimension={{ width: 520, height }}
      className="aspect-auto w-full"
      style={{ height }}
    >
      <BarChart data={rows.map((row) => ({ ...row, fill: color }))} layout="vertical" margin={{ top: 4, right: 88, left: 8, bottom: 4 }}>
        <XAxis type="number" hide />
        <YAxis
          type="category"
          dataKey="label"
          width={112}
          tickLine={false}
          axisLine={false}
          tickFormatter={(value: string) => (value.length > 14 ? `${value.slice(0, 13)}…` : value)}
        />
        <ChartTooltip cursor={{ fill: "var(--paper)" }} content={<RubTip format={format} />} />
        <Bar dataKey="value" fill={color} radius={[0, 8, 8, 0]} barSize={12} animationDuration={700}>
          <LabelList
            dataKey="value"
            position="right"
            formatter={(value) => (typeof value === "number" ? format(value) : "")}
            style={{ fill: "var(--ink)", fontSize: 12 }}
          />
        </Bar>
      </BarChart>
    </ChartContainer>
  );
}

const roleTone: Record<string, string> = {
  Manager: "is-manager",
  Kitchen: "is-kitchen",
  Waiter: "is-waiter",
};

function StaffTable({ rows }: { rows: MoneyReport["staff"] }) {
  const maxPurchase = Math.max(1, ...rows.map((row) => row.purchaseRub));
  return (
    <div className="rep-scroll">
      <table className="rep-table rep-staff">
        <thead>
          <tr><th>Кто</th><th>Роль</th><th className="num">Закупка</th><th className="num">Поставок</th><th className="num">Продажи</th><th className="num">Заказов</th></tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={`${row.name}-${row.role}`}>
              <td>
                <div className="rep-person">
                  <i className={roleTone[row.role]}>{row.name.slice(0, 1)}</i>
                  <span>{row.name}</span>
                </div>
              </td>
              <td><span className={`rep-role ${roleTone[row.role] ?? ""}`}>{roles[row.role] ?? row.role}</span></td>
              <td className="num">
                <div className="rep-money">
                  <strong className={row.purchaseRub === 0 ? "is-zero" : undefined}>{rub(row.purchaseRub)}</strong>
                  {row.purchaseRub > 0 ? (
                    <span className="rep-track" aria-hidden="true">
                      <span className="is-amber" style={{ width: `${Math.max(4, (row.purchaseRub / maxPurchase) * 100)}%` }} />
                    </span>
                  ) : null}
                </div>
              </td>
              <td className={row.deliveries === 0 ? "num is-zero" : "num"}>{row.deliveries}</td>
              <td className={row.salesRub === 0 ? "num is-zero" : "num"}>{rub(row.salesRub)}</td>
              <td className={row.orders === 0 ? "num is-zero" : "num"}>{row.orders}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PurchaseTable({ rows }: { rows: MoneyReport["purchases"] }) {
  const max = Math.max(1, ...rows.map((row) => row.rub));
  return (
    <div className="rep-scroll">
      <table className="rep-table rep-staff">
        <thead><tr><th>Продукт</th><th className="num">Количество</th><th className="num">Сумма</th></tr></thead>
        <tbody>
          {rows.map((row, index) => {
            const color = index < 5 ? sliceColors[index] : "var(--ink-soft)";
            return (
              <tr key={row.productId}>
                <td>
                  <div className="rep-person">
                    <span className="rep-dot" style={{ background: color }} />
                    <span>{row.productName}</span>
                  </div>
                </td>
                <td className="num"><Qty value={row.quantity} unit={row.unit as Unit} /></td>
                <td className="num">
                  <div className="rep-money">
                    <strong className={row.rub === 0 ? "is-zero" : undefined}>{rub(row.rub)}</strong>
                    {row.rub > 0 ? (
                      <span className="rep-track" aria-hidden="true">
                        <span style={{ width: `${Math.max(4, (row.rub / max) * 100)}%`, background: color }} />
                      </span>
                    ) : null}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function Fold({
  title,
  hint,
  open,
  onToggle,
  children,
}: {
  title: string;
  hint: string;
  open: boolean;
  onToggle: (open: boolean) => void;
  children: ReactNode;
}) {
  return (
    <section className="rep-fold">
      <button type="button" className="rep-fold-head" aria-expanded={open} onClick={() => onToggle(!open)}>
        <span>{title}</span>
        <em>{hint}</em>
        <motion.span className="rep-chevron no-print" animate={{ rotate: open ? 180 : 0 }} transition={{ duration: 0.2 }}>
          <ChevronDown size={16} />
        </motion.span>
      </button>
      <motion.div
        className="rep-fold-motion"
        initial={false}
        animate={{ height: open ? "auto" : 0, opacity: open ? 1 : 0 }}
        transition={{ duration: 0.28, ease: [0.2, 0.8, 0.2, 1] }}
      >
        <div className="rep-fold-body">{children}</div>
      </motion.div>
    </section>
  );
}

const reportKinds = [
  { id: "summary", title: "Сводка", hint: "Вся страница: карточки, кольцо, таблицы" },
  { id: "purchases", title: "Закупки", hint: "Продукты, количество и суммы" },
  { id: "staff", title: "Сотрудники", hint: "Кто сколько принял на склад" },
  { id: "dishes", title: "Блюда", hint: "Порции, выручка и себестоимость" },
  { id: "balance", title: "Баланс", hint: "Закупка, выручка, брак и чистыми" },
] as const;

type ReportKind = (typeof reportKinds)[number]["id"];

const kpiMotion = {
  hidden: { opacity: 0, y: 10 },
  show: { opacity: 1, y: 0, transition: { duration: 0.35 } },
};

export function ReportsPage() {
  const { client } = useSession();
  const report = useQuery({ queryKey: ["reports"], queryFn: () => client.reports() });
  const [kind, setKind] = useState<ReportKind>("summary");
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState({
    wheel: true,
    barsBuy: true,
    barsStaff: true,
    staff: false,
    purchases: false,
    dishes: false,
  });
  useEffect(() => {
    if (!menuOpen) return;
    function close(event: MouseEvent) {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false);
    }
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [menuOpen]);
  if (report.isLoading) return <p>Считаем деньги…</p>;
  if (report.error) return <ErrorText error={report.error} />;
  const data = report.data!;
  const topPurchases = data.purchases.slice(0, 8);
  const when = new Intl.DateTimeFormat("ru-RU", { dateStyle: "long", timeStyle: "short" }).format(new Date());
  const current = reportKinds.find((item) => item.id === kind) ?? reportKinds[0];
  function pick(next: ReportKind) {
    setKind(next);
    setMenuOpen(false);
    const tables = next !== "summary";
    setOpen({ wheel: true, barsBuy: true, barsStaff: true, staff: tables, purchases: tables, dishes: tables });
  }
  const allOpen = Object.values(open).every(Boolean);
  function setAll(value: boolean) {
    setOpen({ wheel: value, barsBuy: value, barsStaff: value, staff: value, purchases: value, dishes: value });
  }
  const dishByRevenue = data.dishes.some((row) => row.revenueRub > 0);
  const dishRows = [...data.dishes]
    .sort((a, b) => (dishByRevenue ? b.revenueRub - a.revenueRub : b.portions - a.portions))
    .slice(0, 8)
    .filter((row) => (dishByRevenue ? row.revenueRub > 0 : row.portions > 0))
    .map((row) => ({
      key: row.dishName,
      label: row.dishName,
      value: dishByRevenue ? row.revenueRub : row.portions,
      meta: dishByRevenue ? `${row.portions} порц.` : "порций",
    }));
  const kpis = [
    { label: "Закуплено", value: data.purchaseRub, tone: "", icon: ShoppingCart },
    { label: "Выручка", value: data.revenueRub, tone: "is-sage", icon: Banknote },
    { label: "Себестоимость", value: data.costRub, tone: "is-amber", icon: Receipt },
    { label: "Чистыми", value: data.netRub, tone: "is-rose", icon: Wallet },
    { label: "Брак и срок", value: data.wasteRub, tone: "", icon: Trash2 },
  ];

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Отчёты</h1>
          <p className="print-only">{current.title}. Сформировано {when}</p>
          <p className="no-print">{current.hint}. Брак и срок в прибыль не входят.</p>
        </div>
        <div className="rep-actions no-print">
          <div className="rep-menu" ref={menuRef}>
            <button type="button" className="btn-ghost" aria-expanded={menuOpen} aria-haspopup="listbox" onClick={() => setMenuOpen((value) => !value)}>
              {current.title}
              <ChevronDown size={16} />
            </button>
            {menuOpen ? (
              <ul className="rep-menu-list" role="listbox">
                {reportKinds.map((item) => (
                  <li key={item.id}>
                    <button type="button" className={item.id === kind ? "is-on" : undefined} onClick={() => pick(item.id)}>
                      <strong>{item.title}</strong>
                      <span>{item.hint}</span>
                    </button>
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
          <button type="button" className="btn-ghost" onClick={() => setAll(!allOpen)}>
            {allOpen ? <ChevronsDownUp size={16} /> : <ChevronsUpDown size={16} />}
            {allOpen ? "Свернуть всё" : "Развернуть всё"}
          </button>
          <button type="button" className="btn" onClick={() => window.print()}>
            <Printer size={16} />
            Печать / PDF
          </button>
          <button type="button" className="btn-ghost" onClick={() => downloadCsv(data)}>
            <Download size={16} />
            Скачать CSV
          </button>
        </div>
      </header>

      {kind === "summary" || kind === "balance" ? (
      <motion.section className="rep-kpis" initial="hidden" animate="show" variants={{ show: { transition: { staggerChildren: 0.06 } } }}>
        {kpis.map((item) => (
          <motion.article key={item.label} className={item.tone} variants={kpiMotion}>
            <div className="rep-kpi-top">
              <span>{item.label}</span>
              <item.icon size={16} />
            </div>
            <strong>{rub(item.value)}</strong>
          </motion.article>
        ))}
      </motion.section>
      ) : null}

      {kind === "summary" ? (
      <>
      <section className="rep-grid">
        <Fold title="Куда ушла закупка" hint={rub(data.purchaseRub)} open={open.wheel} onToggle={(value) => setOpen((current) => ({ ...current, wheel: value }))}>
          <Wheel data={data} />
        </Fold>
        <Fold title="Сотрудники, закупка" hint={rub(data.purchaseRub)} open={open.barsStaff} onToggle={(value) => setOpen((current) => ({ ...current, barsStaff: value }))}>
          <MoneyBars
            color="var(--amber)"
            valueLabel="Закупка"
            rows={data.staff.filter((row) => row.purchaseRub > 0).map((row) => ({
              key: row.name,
              label: row.name,
              value: row.purchaseRub,
              meta: `${roles[row.role] ?? row.role} · поставок ${row.deliveries}`,
            }))}
          />
        </Fold>
        <div className="rep-span">
          <Fold title="Что закуплено" hint={`${topPurchases.length} из ${data.purchases.length}`} open={open.barsBuy} onToggle={(value) => setOpen((current) => ({ ...current, barsBuy: value }))}>
            <MoneyBars
              color="var(--accent)"
              valueLabel="Сумма"
              rows={topPurchases.map((row) => ({ key: row.productId, label: row.productName, value: row.rub }))}
            />
          </Fold>
        </div>
      </section>
      <Fold title="Сотрудники" hint={String(data.staff.length)} open={open.staff} onToggle={(value) => setOpen((current) => ({ ...current, staff: value }))}>
        {data.staff.length === 0 ? <Empty>Пока нет ни приходов, ни заказов.</Empty> : (
          <StaffTable rows={data.staff} />
        )}
      </Fold>
      <section className="rep-grid">
        <Fold title="Все закупки" hint={String(data.purchases.length)} open={open.purchases} onToggle={(value) => setOpen((current) => ({ ...current, purchases: value }))}>
          <PurchaseTable rows={data.purchases} />
        </Fold>
        <Fold title="Блюда" hint={data.dishes.length === 0 ? "нет заказов" : rub(data.revenueRub)} open={open.dishes} onToggle={(value) => setOpen((current) => ({ ...current, dishes: value }))}>
          {data.dishes.length === 0 ? <Empty>Принятых заказов ещё нет — выручка появится после первого чека.</Empty> : (
            <>
              {dishRows.length > 0 ? (
                <MoneyBars
                  color="var(--sage)"
                  valueLabel={dishByRevenue ? "Выручка" : "Порций"}
                  format={dishByRevenue ? rub : (value) => String(value)}
                  rows={dishRows}
                />
              ) : null}
              <div className="rep-scroll">
                <table className="rep-table">
                  <thead><tr><th>Блюдо</th><th className="num">Цена</th><th className="num">Порций</th><th className="num">Выручка</th><th className="num">Себест.</th><th className="num">Чистыми</th></tr></thead>
                  <tbody>
                    {data.dishes.map((row) => (
                      <tr key={row.dishName}>
                        <td>{row.dishName}</td>
                        <td className="num">{rub(row.salePrice)}</td>
                        <td className="num">{row.portions}</td>
                        <td className="num">{rub(row.revenueRub)}</td>
                        <td className="num">{rub(row.costRub)}</td>
                        <td className="num">{rub(row.netRub)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </Fold>
      </section>
      </>
      ) : null}

      {kind === "staff" ? (
      <>
      <Fold title="Сотрудники, закупка" hint={rub(data.purchaseRub)} open={open.barsStaff} onToggle={(value) => setOpen((current) => ({ ...current, barsStaff: value }))}>
        <MoneyBars
          color="var(--amber)"
          valueLabel="Закупка"
          rows={data.staff.filter((row) => row.purchaseRub > 0).map((row) => ({
            key: row.name,
            label: row.name,
            value: row.purchaseRub,
            meta: `${roles[row.role] ?? row.role} · поставок ${row.deliveries}`,
          }))}
        />
      </Fold>
      <Fold title="Сотрудники" hint={String(data.staff.length)} open={open.staff} onToggle={(value) => setOpen((current) => ({ ...current, staff: value }))}>
        {data.staff.length === 0 ? <Empty>Пока нет ни приходов, ни заказов.</Empty> : (
          <StaffTable rows={data.staff} />
        )}
      </Fold>
      </>
      ) : null}

      {kind === "purchases" ? (
      <>
      <Fold title="Что закуплено" hint={`${topPurchases.length} из ${data.purchases.length}`} open={open.barsBuy} onToggle={(value) => setOpen((current) => ({ ...current, barsBuy: value }))}>
        <MoneyBars
          color="var(--accent)"
          valueLabel="Сумма"
          rows={topPurchases.map((row) => ({ key: row.productId, label: row.productName, value: row.rub }))}
        />
      </Fold>
      <Fold title="Все закупки" hint={String(data.purchases.length)} open={open.purchases} onToggle={(value) => setOpen((current) => ({ ...current, purchases: value }))}>
        <PurchaseTable rows={data.purchases} />
      </Fold>
      </>
      ) : null}

      {kind === "dishes" ? (
      <Fold title="Блюда" hint={data.dishes.length === 0 ? "нет заказов" : rub(data.revenueRub)} open={open.dishes} onToggle={(value) => setOpen((current) => ({ ...current, dishes: value }))}>
          {data.dishes.length === 0 ? <Empty>Принятых заказов ещё нет — выручка появится после первого чека.</Empty> : (
            <>
              {dishRows.length > 0 ? (
                <MoneyBars
                  color="var(--sage)"
                  valueLabel={dishByRevenue ? "Выручка" : "Порций"}
                  format={dishByRevenue ? rub : (value) => String(value)}
                  rows={dishRows}
                />
              ) : null}
              <div className="rep-scroll">
                <table className="rep-table">
                  <thead><tr><th>Блюдо</th><th className="num">Цена</th><th className="num">Порций</th><th className="num">Выручка</th><th className="num">Себест.</th><th className="num">Чистыми</th></tr></thead>
                  <tbody>
                    {data.dishes.map((row) => (
                      <tr key={row.dishName}>
                        <td>{row.dishName}</td>
                        <td className="num">{rub(row.salePrice)}</td>
                        <td className="num">{row.portions}</td>
                        <td className="num">{rub(row.revenueRub)}</td>
                        <td className="num">{rub(row.costRub)}</td>
                        <td className="num">{rub(row.netRub)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
      </Fold>
      ) : null}

      {kind === "balance" ? (
      <Fold title="Из чего сложились деньги" hint={rub(data.netRub)} open={open.wheel} onToggle={(value) => setOpen((current) => ({ ...current, wheel: value }))}>
        <MoneyBars
          color="var(--sage)"
          valueLabel="Сумма"
          rows={[
            { key: "purchase", label: "Закуплено", value: data.purchaseRub },
            { key: "revenue", label: "Выручка", value: data.revenueRub },
            { key: "cost", label: "Себестоимость", value: data.costRub },
            { key: "waste", label: "Брак и срок", value: data.wasteRub },
            { key: "net", label: "Чистыми", value: Math.max(0, data.netRub) },
          ].filter((row) => row.value > 0)}
        />
      </Fold>
      ) : null}
    </div>
  );
}
