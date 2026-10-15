import type { ExpiryWriteOffLine } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { PendingWriteOffs } from "../pending";
import { useSession } from "../session";
import { DishTile, Empty, ErrorText, formatWhen, FreshnessPill, Hours, Panel, Qty, isLow } from "../ui";

function rub(value: number) {
  return new Intl.NumberFormat("ru-RU", { style: "currency", currency: "RUB", maximumFractionDigits: 0 }).format(value);
}

export function ExpiredWriteOffTable({ rows }: { rows: ExpiryWriteOffLine[] }) {
  const loss = rows.reduce((sum, row) => sum + row.loss, 0);
  return (
    <div className="rep-scroll">
      <table className="rep-table">
        <thead><tr><th>Продукт</th><th>Партия</th><th className="num">Снято</th><th>Годен до</th><th>Списано</th><th className="num">Потеря</th></tr></thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.batchId}>
              <td><Link to={`/stock/${row.productId}`}>{row.productName}</Link></td>
              <td>{row.deliveryNumber}</td>
              <td className="num"><Qty value={row.quantity} unit={row.unit} /></td>
              <td>{formatWhen(row.expiresAt)}</td>
              <td>{formatWhen(row.writtenOffAt)}</td>
              <td className="num">{rub(row.loss)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="reason">Со склада ушло, в остатке этих партий нет. Потеря закупки {rub(loss)}.</p>
    </div>
  );
}

export function DashboardPage() {
  const { client } = useSession();
  const dashboard = useQuery({ queryKey: ["dashboard"], queryFn: () => client.dashboard() });
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const priorityQuery = useQuery({ queryKey: ["priority"], queryFn: () => client.priority() });

  if (dashboard.isLoading) return <p>Считаем склад…</p>;
  if (dashboard.error) return <ErrorText error={dashboard.error} />;
  const data = dashboard.data!;
  const stopped = stop.data?.filter((entry) => entry.isStopped) ?? [];
  const low = stop.data?.filter(isLow) ?? [];
  const priority = priorityQuery.data ?? [];
  const expired = data.expired ?? [];

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Сводка</h1>
          <p>
            Экран менеджера на открытие и закрытие смены: что в 86, какие партии горят, что уже списали по сроку.
            Стоп: шеф, ноль заготовки или склад. Приоритет ставит шеф на «Смене». Здесь только смотреть.
          </p>
        </div>
      </div>
      <PendingWriteOffs />
      <section className="stats stats-five">
        <article className="stat stat-rose"><span>Стоп</span><strong>{stopped.length}</strong></article>
        <article className="stat stat-amber"><span>Мало</span><strong>{low.length}</strong></article>
        <article className="stat stat-amber"><span>Истекает за сутки</span><strong>{data.expiring.length}</strong></article>
        <article className="stat stat-rose"><span>Списано по сроку</span><strong>{expired.length}</strong></article>
        <article className="stat stat-sage"><span>Заказов сегодня</span><strong>{data.acceptedOrdersToday}</strong></article>
      </section>
      <Panel title="Снято со склада по сроку">
        <p className="reason">Фон сам обнуляет партию, когда «годен до» прошёл. Это уже потеря, не остаток. Истекающие ниже — ещё можно успеть продать.</p>
        {expired.length === 0 ? <Empty>Просроченных партий нет. Когда срок выйдет, строка появится здесь сама.</Empty> : <ExpiredWriteOffTable rows={expired} />}
      </Panel>
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
        <p className="reason">Только то, что шеф подтвердил на смене. Подсказка системы зал не видит.</p>
        {priorityQuery.isLoading ? <p>Загрузка…</p> : null}
        {priority.length === 0 ? <Empty>Шеф ещё не отметил, что предлагать.</Empty> : (
          <div className="tiles">
            {priority.map((entry) => <DishTile key={entry.dishId} entry={entry} priority />)}
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
    </>
  );
}
