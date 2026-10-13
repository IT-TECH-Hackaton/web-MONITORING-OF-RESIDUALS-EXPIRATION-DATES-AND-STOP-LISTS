import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { useSession } from "../session";
import { DishTile, Empty, ErrorText, FreshnessPill, Hours, Panel, Qty, isLow } from "../ui";

export function DashboardPage() {
  const { client } = useSession();
  const dashboard = useQuery({ queryKey: ["dashboard"], queryFn: () => client.dashboard() });
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });

  if (dashboard.isLoading) return <p>Считаем склад…</p>;
  if (dashboard.error) return <ErrorText error={dashboard.error} />;
  const data = dashboard.data!;
  const stopped = stop.data?.filter((entry) => entry.isStopped) ?? [];
  const low = stop.data?.filter(isLow) ?? [];
  const priority = offers.data?.slice(0, 4) ?? [];

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Сводка</h1>
          <p>
            Экран менеджера на открытие и закрытие смены: что в 86, какие партии горят, что уже списали по сроку.
            Стоп и приоритет ставит шеф на «Смене», здесь только смотреть.
          </p>
        </div>
      </div>
      <section className="stats">
        <article className="stat stat-rose"><span>Стоп</span><strong>{stopped.length}</strong></article>
        <article className="stat stat-amber"><span>Мало</span><strong>{low.length}</strong></article>
        <article className="stat stat-amber"><span>Истекает за сутки</span><strong>{data.expiring.length}</strong></article>
        <article className="stat stat-sage"><span>Заказов сегодня</span><strong>{data.acceptedOrdersToday}</strong></article>
      </section>
      <Panel title="Стоп">
        {stop.isLoading ? <p>Загрузка…</p> : null}
        {stopped.length === 0 ? <Empty>Ничего не закрыто. Зал может брать все блюда.</Empty> : (
          <div className="tiles">
            {stopped.map((entry) => <DishTile key={entry.dishId} entry={entry} showReason />)}
          </div>
        )}
      </Panel>
      <Panel title="Мало — до 5 порций">
        {low.length === 0 ? <Empty>Нет блюд на исходе заготовки.</Empty> : (
          <div className="tiles">
            {low.map((entry) => <DishTile key={entry.dishId} entry={entry} />)}
          </div>
        )}
      </Panel>
      <Panel title="Приоритет продажи">
        <p className="reason">Пока считает система по короткому сроку партий. Шеф подтвердит список на «Смене».</p>
        {offers.isLoading ? <p>Загрузка…</p> : null}
        {priority.length === 0 ? <Empty>Нет блюд, которыми стоит спасать срок.</Empty> : (
          <div className="stack">
            {priority.map((offer, index) => (
              <article key={offer.dishId}>
                <strong>{index + 1}. {offer.dishName}</strong>
                <p className="reason">{offer.reason}</p>
              </article>
            ))}
          </div>
        )}
      </Panel>
      <Panel title="Истекает за сутки">
        {data.expiring.length === 0 ? <Empty>В ближайшие сутки ничего не истекает.</Empty> : (
          <table>
            <thead><tr><th>Продукт</th><th>Партия</th><th>Остаток</th><th>Осталось</th><th></th></tr></thead>
            <tbody>
              {data.expiring.map((batch) => (
                <tr key={batch.id}>
                  <td><Link to={`/stock/${batch.productId}`}>{batch.productName}</Link></td>
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
      <div className="split">
        <Panel title="Списано по сроку сегодня">
          {data.expiryWriteOffToday.length === 0 ? (
            <Empty>Автосписаний за сегодня нет.</Empty>
          ) : (
            <div className="stack">
              {data.expiryWriteOffToday.map((row) => (
                <article key={row.productName}>
                  <strong>{row.productName}</strong>
                  <p className="reason"><Qty value={row.quantity} unit={row.unit} /></p>
                </article>
              ))}
            </div>
          )}
        </Panel>
        <Panel title="Скоро уйдёт в стоп">
          {data.forecasts.length === 0 ? (
            <Empty>Пока нет расхода, по которому оценить момент стопа.</Empty>
          ) : (
            <div className="stack">
              {data.forecasts.map((forecast) => (
                <article key={`${forecast.dishName}-${forecast.productName}`}>
                  <strong>{forecast.dishName}</strong>
                  <p className="reason">{forecast.productName} — примерно через {forecast.hoursUntilStop} ч, если темп заказов сохранится.</p>
                </article>
              ))}
            </div>
          )}
        </Panel>
      </div>
    </>
  );
}
