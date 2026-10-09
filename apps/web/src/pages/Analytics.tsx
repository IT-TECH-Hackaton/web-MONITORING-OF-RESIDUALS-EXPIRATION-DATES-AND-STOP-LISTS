import { useQuery } from "@tanstack/react-query";
import { useSession } from "../session";
import { Empty, ErrorText, Panel, Qty } from "../ui";
import { WeeklyForecastPanel } from "./WeeklyForecast";

export function AnalyticsPage() {
  const { client } = useSession();
  const writeOffs = useQuery({ queryKey: ["write-offs"], queryFn: () => client.writeOffs() });
  const deficits = useQuery({ queryKey: ["deficits"], queryFn: () => client.deficits() });
  const weekly = useQuery({ queryKey: ["weekly-forecast"], queryFn: () => client.weeklyForecast() });
  const max = Math.max(1, ...(writeOffs.data?.map((row) => row.quantity) ?? [1]));
  return (
    <>
      <div className="page-title">
        <div>
          <h1>Аналитика</h1>
          <p>Списания, дефицит и прогноз: чего по темпу этой недели не хватит на следующую.</p>
        </div>
      </div>
      {weekly.data ? <WeeklyForecastPanel report={weekly.data} /> : null}
      {weekly.error ? <ErrorText error={weekly.error} /> : null}
      <div className="split">
        <Panel title="Списания">
          {writeOffs.isLoading ? <p>Загрузка…</p> : null}
          {writeOffs.error ? <ErrorText error={writeOffs.error} /> : null}
          {writeOffs.data?.length === 0 ? <Empty>Движений списания пока нет.</Empty> : null}
          <div className="stack">
            {writeOffs.data?.map((row) => (
              <article key={`${row.productId}-${row.type}`}>
                <strong>{row.productName}</strong>
                <p className="reason">{row.type === "ExpiryWriteOff" ? "По сроку" : row.type === "KitchenWriteOff" ? "Брак с кухни" : "В заказы"} · <Qty value={row.quantity} unit={row.unit} /></p>
                <div className="bar"><span style={{ width: `${(row.quantity / max) * 100}%` }} /></div>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="Дефицит">
          {deficits.isLoading ? <p>Загрузка…</p> : null}
          {deficits.error ? <ErrorText error={deficits.error} /> : null}
          {deficits.data?.length === 0 ? <Empty>Дефицитных продуктов нет.</Empty> : null}
          <table>
            <thead><tr><th>Продукт</th><th>Отказов</th><th>Сумма дефицита</th><th>Сейчас</th></tr></thead>
            <tbody>
              {deficits.data?.map((row) => (
                <tr key={row.productId}>
                  <td>{row.productName}</td>
                  <td>{row.rejectionCount}</td>
                  <td><Qty value={row.totalDeficit} unit={row.unit} /></td>
                  <td><Qty value={row.available} unit={row.unit} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      </div>
    </>
  );
}
