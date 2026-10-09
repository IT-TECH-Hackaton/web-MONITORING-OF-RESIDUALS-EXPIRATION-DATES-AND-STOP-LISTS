import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, FreshnessPill, Hours, Panel, Qty } from "../ui";
import { forecastBasis, PurchaseButton } from "./PurchaseQr";
import { WeeklyForecastPanel } from "./WeeklyForecast";

export function DashboardPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const dashboard = useQuery({ queryKey: ["dashboard"], queryFn: () => client.dashboard() });
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });
  const simulateDelivery = useMutation({
    mutationFn: () => client.simulateDelivery(),
    onSuccess: async () => {
      await queryClient.invalidateQueries();
    },
  });
  const simulateOrder = useMutation({
    mutationFn: () => client.simulateOrder(),
    onSuccess: async () => {
      await queryClient.invalidateQueries();
    },
  });

  if (dashboard.isLoading) return <p>Считаем склад…</p>;
  if (dashboard.error) return <ErrorText error={dashboard.error} />;
  const data = dashboard.data!;
  const writtenOff = data.expiryWriteOffToday.reduce((sum, row) => sum + row.quantity, 0);

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Сводка смены</h1>
          <p>Что истекает, что уже в стопе и чего по темпу этой недели не хватит на следующую.</p>
        </div>
        <div className="btn-row">
          <button className="btn" onClick={() => simulateDelivery.mutate()} disabled={simulateDelivery.isPending}>Имитация поставки</button>
          <button className="btn-ghost" onClick={() => simulateOrder.mutate()} disabled={simulateOrder.isPending}>Имитация заказа</button>
        </div>
      </div>
      {simulateOrder.data?.status === "Rejected" ? <p className="error-text">{simulateOrder.data.rejectionReason}</p> : null}
      <Panel title="К смене">
        <div className="brief">
          <article>
            <strong>Истекает в ближайшие сутки</strong>
            <p className="reason">
              {data.expiring.length === 0
                ? "Таких партий нет."
                : data.expiring.map((batch) => `${batch.productName} (${batch.deliveryNumber})`).join(", ")}
            </p>
          </article>
          <article>
            <strong>Уже в стопе</strong>
            {stop.data?.filter((entry) => entry.isStopped).length ? (
              stop.data.filter((entry) => entry.isStopped).map((entry) => (
                <p className="reason" key={entry.dishId}>{entry.dishName}: {entry.reason}</p>
              ))
            ) : <p className="reason">Все блюда можно готовить.</p>}
          </article>
          <article>
            <strong>Предложить гостю</strong>
            {offers.data?.slice(0, 3).length ? offers.data.slice(0, 3).map((offer) => (
              <p className="reason" key={offer.dishId}>{offer.dishName}. {offer.reason}</p>
            )) : <p className="reason">Сейчас нет блюд, которыми стоит спасать короткий срок.</p>}
          </article>
          <article>
            <strong>На следующую неделю не хватит: {data.weeklyForecast.items.filter((item) => item.shortfall > 0).length}</strong>
            <p className="reason">{forecastBasis(data.weeklyForecast.ordersThisWeek)}</p>
            {data.weeklyForecast.items.some((item) => item.shortfall > 0) ? <PurchaseButton label="В заявку" /> : null}
          </article>
        </div>
      </Panel>
      <section className="stats">
        <article className="stat stat-amber"><span>Истекает за сутки</span><strong>{data.expiring.length}</strong></article>
        <article className="stat stat-rose"><span>Блюд в стопе</span><strong>{data.stoppedCount}</strong></article>
        <article className="stat stat-ink"><span>Списано по сроку сегодня</span><strong>{writtenOff.toLocaleString("ru-RU")}</strong></article>
        <article className="stat stat-sage"><span>Принятых заказов сегодня</span><strong>{data.acceptedOrdersToday}</strong></article>
      </section>
      <div className="split">
        <Panel title="Партии с коротким сроком">
          {data.expiring.length === 0 ? <Empty>В ближайшие сутки ничего не истекает.</Empty> : (
            <table>
              <thead><tr><th>Продукт</th><th>Партия</th><th>Остаток</th><th>Осталось</th><th></th></tr></thead>
              <tbody>
                {data.expiring.map((batch) => (
                  <tr key={batch.id}>
                    <td>{batch.productName}</td>
                    <td>{batch.deliveryNumber}</td>
                    <td><Qty value={batch.quantityRemaining} unit={batch.unit} /></td>
                    <td><Hours value={batch.hoursLeft} /></td>
                    <td><FreshnessPill freshness={batch.freshness} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>
        <Panel title="Прогноз стоп-листа" action={<Link to="/offers">Горячие блюда</Link>}>
          {data.forecasts.length === 0 ? <Empty>Пока нет расхода, по которому можно оценить момент стопа.</Empty> : (
            <div className="stack">
              {data.forecasts.map((forecast) => (
                <article key={`${forecast.dishName}-${forecast.productName}`}>
                  <strong>{forecast.dishName}</strong>
                  <p className="reason">{forecast.productName} может увести блюдо в стоп примерно через {forecast.hoursUntilStop} ч, если заказы сохранятся.</p>
                </article>
              ))}
            </div>
          )}
        </Panel>
      </div>
      <WeeklyForecastPanel report={data.weeklyForecast} shortagesOnly />
    </>
  );
}
