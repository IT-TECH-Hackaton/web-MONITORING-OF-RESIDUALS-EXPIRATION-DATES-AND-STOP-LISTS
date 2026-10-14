import { useQuery } from "@tanstack/react-query";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, Panel } from "../ui";

export function OrdersPage() {
  const { client } = useSession();
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => client.orders() });
  return (
    <>
      <div className="page-title">
        <div>
          <h1>Заказы</h1>
          <p>Журнал зала: стол, состав, принят или отказ. Отклонённый заказ склад не меняет.</p>
        </div>
      </div>
      <Panel title="Журнал">
        {orders.isLoading ? <p>Загрузка…</p> : null}
        {orders.error ? <ErrorText error={orders.error} /> : null}
        {orders.data?.length === 0 ? <Empty>Заказов ещё не было.</Empty> : null}
        {orders.data && orders.data.length > 0 ? (
          <table>
            <thead><tr><th>Время</th><th>Стол</th><th>Кто</th><th>Состав</th><th>Статус</th><th>Причина</th></tr></thead>
            <tbody>
              {orders.data.map((order) => (
                <tr key={order.id}>
                  <td>{formatWhen(order.createdAt)}</td>
                  <td>{order.tableNumber ?? "—"}</td>
                  <td>{order.createdByLogin}</td>
                  <td>{order.items.map((item) => `${item.dishName} × ${item.portions}`).join(", ")}</td>
                  <td><span className={`pill pill-${order.status.toLowerCase()}`}>{order.status === "Accepted" ? "Принят" : "Отказ"}</span></td>
                  <td className="reason">{order.rejectionReason ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </Panel>
    </>
  );
}
