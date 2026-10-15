import { formatQty, type KitchenStatus, type Order, type OrderLine } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, Panel } from "../ui";

function statusLabel(status: KitchenStatus) {
  if (status === "Cooking") return "Готовится";
  if (status === "Ready") return "Готово";
  if (status === "Spoiled") return "Испорчено";
  return "В очереди";
}

export function KitchenOrdersPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => client.orders(), refetchInterval: 5000 });
  const mark = useMutation({
    mutationFn: (input: { orderId: string; itemId: string; status: "Cooking" | "Ready" | "Spoiled"; note?: string }) =>
      input.status === "Spoiled"
        ? client.markDish(input.orderId, input.itemId, "Spoiled", input.note ?? "")
        : client.markKitchenDish(input.orderId, input.itemId, input.status),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["orders"] });
      await queryClient.invalidateQueries({ queryKey: ["stock"] });
    },
  });
  const open = (orders.data ?? []).filter((order) => order.status === "Accepted" && order.items.some((item) => item.kitchenStatus !== "Ready"));

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Заказы</h1>
          <p>Официант пробил стол. «В работу» списывает техкарту со склада один раз. «Готово» склад больше не трогает.</p>
        </div>
      </header>
      {orders.isLoading ? <p>Загрузка…</p> : null}
      {orders.error ? <ErrorText error={orders.error} /> : null}
      {mark.error ? <ErrorText error={mark.error} /> : null}
      {!orders.isLoading && open.length === 0 ? <Empty>Сейчас нет заказов в работе.</Empty> : null}
      {open.map((order) => (
        <OrderCard key={order.id} order={order} pending={mark.isPending} onMark={(itemId, status, note) => mark.mutate({ orderId: order.id, itemId, status, note })} />
      ))}
    </div>
  );
}

function DishLine({ item, pending, onMark }: { item: OrderLine; pending: boolean; onMark: (itemId: string, status: "Cooking" | "Ready" | "Spoiled", note?: string) => void }) {
  const [spoil, setSpoil] = useState(false);
  const [text, setText] = useState("");
  return (
    <article className="del-step">
      <div className="del-step-head">
        <div>
          <strong>{item.dishName} × {item.portions}</strong>
          <p className="reason">{statusLabel(item.kitchenStatus)}</p>
        </div>
        {item.kitchenStatus === "Queued" ? (
          <button type="button" className="btn" disabled={pending} onClick={() => onMark(item.id, "Cooking")}>В работу</button>
        ) : null}
        {item.kitchenStatus === "Cooking" ? (
          <button type="button" className="btn" disabled={pending} onClick={() => onMark(item.id, "Ready")}>Готово</button>
        ) : null}
        {item.kitchenStatus !== "Spoiled" && item.kitchenStatus !== "Ready" ? (
          <button type="button" className="btn-ghost" disabled={pending} onClick={() => setSpoil((current) => !current)}>Испорчено</button>
        ) : null}
      </div>
      {spoil ? (
        <form className="form" onSubmit={(event) => { event.preventDefault(); if (text.trim()) onMark(item.id, "Spoiled", text.trim()); }}>
          <label>
            Что сказать официанту
            <input value={text} onChange={(event) => setText(event.target.value)} placeholder="Подгорело, гостю лучше заменить" maxLength={300} required />
          </label>
          <button className="btn" type="submit" disabled={pending || text.trim().length === 0}>Отправить официанту</button>
        </form>
      ) : null}
      {item.needs && item.needs.length > 0 && item.kitchenStatus !== "Spoiled" ? (
        <ul className="del-lines">
          {item.needs.map((need) => (
            <li key={need.productName}>Спишется {need.productName} {formatQty(need.quantity, need.unit)}</li>
          ))}
        </ul>
      ) : null}
      {item.kitchenNote ? <p className="error-text">{item.kitchenNote}</p> : null}
    </article>
  );
}

function OrderCard({ order, pending, onMark }: { order: Order; pending: boolean; onMark: (itemId: string, status: "Cooking" | "Ready" | "Spoiled", note?: string) => void }) {
  return (
    <Panel title={order.tableNumber ? `Стол ${order.tableNumber}` : "Без стола"}>
      <p className="reason">{formatWhen(order.createdAt)} · {order.createdByLogin}</p>
      <div className="stack">
        {order.items.map((item) => (
          <DishLine key={item.id} item={item} pending={pending} onMark={onMark} />
        ))}
      </div>
      {order.writeOffs && order.writeOffs.length > 0 ? (
        <p className="reason">{order.writeOffs.map((line) => `${line.productName} ${line.before} − ${line.quantity} = ${line.after}`).join("; ")}</p>
      ) : null}
    </Panel>
  );
}
