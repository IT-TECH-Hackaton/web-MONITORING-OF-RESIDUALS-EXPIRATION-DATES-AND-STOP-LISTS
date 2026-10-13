import type { MoneyReport, Unit } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "../session";
import { Empty, ErrorText, Panel, Qty } from "../ui";

const roles: Record<string, string> = {
  Manager: "менеджер",
  Kitchen: "кухня",
  Waiter: "официант",
};

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

export function ReportsPage() {
  const { client } = useSession();
  const report = useQuery({ queryKey: ["reports"], queryFn: () => client.reports() });
  if (report.isLoading) return <p>Считаем деньги…</p>;
  if (report.error) return <ErrorText error={report.error} />;
  const data = report.data!;

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Отчёты</h1>
          <p className="print-only">Сформировано {new Intl.DateTimeFormat("ru-RU", { dateStyle: "long", timeStyle: "short" }).format(new Date())}</p>
          <p className="no-print">Закупка — кто принял поставку и на какую сумму. Выручка — цена меню на принятые порции. Чистыми — выручка минус себестоимость того, что ушло в заказы.</p>
        </div>
        <div className="btn-row no-print">
          <button type="button" className="btn" onClick={() => window.print()}>Печать / PDF</button>
          <button type="button" className="btn-ghost" onClick={() => downloadCsv(data)}>Скачать CSV</button>
        </div>
      </div>
      <section className="stats">
        <article className="stat"><span>Закуплено</span><strong>{rub(data.purchaseRub)}</strong></article>
        <article className="stat stat-sage"><span>Выручка</span><strong>{rub(data.revenueRub)}</strong></article>
        <article className="stat stat-amber"><span>Себестоимость</span><strong>{rub(data.costRub)}</strong></article>
        <article className="stat stat-rose"><span>Чистыми</span><strong>{rub(data.netRub)}</strong></article>
      </section>
      <p className="reason">Брак и срок списаны ещё на {rub(data.wasteRub)}. В «чистыми» это не входит: это не продажа.</p>
      <Panel title="Сотрудники">
        {data.staff.length === 0 ? <Empty>Пока нет ни приходов, ни заказов.</Empty> : (
          <table>
            <thead><tr><th>Кто</th><th>Роль</th><th>Закупка</th><th>Поставок</th><th>Продажи</th><th>Заказов</th></tr></thead>
            <tbody>
              {data.staff.map((row) => (
                <tr key={row.name}>
                  <td>{row.name}</td>
                  <td>{roles[row.role] ?? row.role}</td>
                  <td>{rub(row.purchaseRub)}</td>
                  <td>{row.deliveries}</td>
                  <td>{rub(row.salesRub)}</td>
                  <td>{row.orders}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
      <div className="split">
        <Panel title="Что закуплено">
          <table>
            <thead><tr><th>Продукт</th><th>Количество</th><th>Сумма</th></tr></thead>
            <tbody>
              {data.purchases.map((row) => (
                <tr key={row.productId}>
                  <td>{row.productName}</td>
                  <td><Qty value={row.quantity} unit={row.unit as Unit} /></td>
                  <td>{rub(row.rub)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
        <Panel title="Блюда">
          {data.dishes.length === 0 ? <Empty>Принятых заказов ещё нет — выручка появится после первого чека.</Empty> : (
            <table>
              <thead><tr><th>Блюдо</th><th>Цена</th><th>Порций</th><th>Выручка</th><th>Себест.</th><th>Чистыми</th></tr></thead>
              <tbody>
                {data.dishes.map((row) => (
                  <tr key={row.dishName}>
                    <td>{row.dishName}</td>
                    <td>{rub(row.salePrice)}</td>
                    <td>{row.portions}</td>
                    <td>{rub(row.revenueRub)}</td>
                    <td>{rub(row.costRub)}</td>
                    <td>{rub(row.netRub)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>
      </div>
    </>
  );
}
