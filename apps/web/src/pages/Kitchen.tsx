import { formatHours, formatQty, type KitchenStatus } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, Panel } from "../ui";

const marks: { status: KitchenStatus; label: string }[] = [
  { status: "Queued", label: "В очереди" },
  { status: "Cooking", label: "Готовится" },
  { status: "Ready", label: "Готово" },
];

export function KitchenPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const board = useQuery({ queryKey: ["kitchen"], queryFn: () => client.kitchen() });
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [writeOff, setWriteOff] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  const markDish = useMutation({
    mutationFn: (input: { orderId: string; itemId: string; status: KitchenStatus; note: string }) =>
      client.markDish(input.orderId, input.itemId, input.status, input.note),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["kitchen"] });
      await queryClient.invalidateQueries({ queryKey: ["orders"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Отметку не сохранили"),
  });

  const markBatch = useMutation({
    mutationFn: (input: { batchId: string; opened: boolean; note: string; writeOffQuantity: number | null }) =>
      client.markBatch(input.batchId, input.opened, input.note, input.writeOffQuantity),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["kitchen"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Партию не отметили"),
  });

  if (board.isLoading) return <p>Собираем экран кухни…</p>;
  if (board.error) return <ErrorText error={board.error} />;
  const data = board.data!;
  const openOrders = data.orders.filter((order) => order.items.some((item) => item.kitchenStatus !== "Ready"));
  const readyOrders = data.orders.filter((order) => order.items.length > 0 && order.items.every((item) => item.kitchenStatus === "Ready"));

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Кухня</h1>
          <p>Отмечайте ход блюда и состояние партии. Списание брака сразу уходит со склада.</p>
        </div>
      </div>
      {error ? <p className="error-text">{error}</p> : null}
      <div className="split">
        <Panel title="Заказы в работе">
          {openOrders.length === 0 ? <Empty>В очереди пусто. Готовые заказы можно отдавать.</Empty> : null}
          <div className="stack">
            {openOrders.map((order) => (
              <article key={order.id} className="brief">
                <div>
                  <strong>{formatWhen(order.createdAt)} · {order.createdByLogin}</strong>
                  {order.items.map((item) => (
                    <div key={item.id} className="stack">
                      <p className="reason">{item.dishName} × {item.portions}</p>
                      <div className="btn-row">
                        {marks.map((mark) => (
                          <button
                            key={mark.status}
                            className={item.kitchenStatus === mark.status ? "btn" : "btn-ghost"}
                            type="button"
                            onClick={() => markDish.mutate({
                              orderId: order.id,
                              itemId: item.id,
                              status: mark.status,
                              note: notes[item.id] ?? item.kitchenNote ?? "",
                            })}
                          >
                            {mark.label}
                          </button>
                        ))}
                      </div>
                      <input
                        placeholder="Как готовим: без лука, прожарка..."
                        value={notes[item.id] ?? item.kitchenNote ?? ""}
                        onChange={(event) => setNotes((current) => ({ ...current, [item.id]: event.target.value }))}
                      />
                    </div>
                  ))}
                </div>
              </article>
            ))}
          </div>
          {readyOrders.length > 0 ? <p className="hint">Готово к отдаче: {readyOrders.length}. Зал видит это на вкладке «Отдача».</p> : null}
        </Panel>
        <Panel title="Партии, которые надо заметить">
          {data.batches.length === 0 ? <Empty>Нет вскрытых и коротких партий.</Empty> : null}
          <div className="stack">
            {data.batches.map((batch) => (
              <article key={batch.batchId}>
                <strong>{batch.productName}</strong>
                <p className="reason">
                  {batch.deliveryNumber} · осталось {formatQty(batch.remaining, batch.unit)} · {formatHours(batch.hoursLeft)}
                  {batch.openedAt ? " · вскрыта" : ""}
                </p>
                <input
                  placeholder="Заметка кухни"
                  value={notes[batch.batchId] ?? batch.note ?? ""}
                  onChange={(event) => setNotes((current) => ({ ...current, [batch.batchId]: event.target.value }))}
                />
                <input
                  type="number"
                  min="0"
                  step="0.001"
                  placeholder="Списать брак"
                  value={writeOff[batch.batchId] ?? ""}
                  onChange={(event) => setWriteOff((current) => ({ ...current, [batch.batchId]: event.target.value }))}
                />
                <div className="btn-row">
                  <button
                    className="btn-ghost"
                    type="button"
                    onClick={() => markBatch.mutate({
                      batchId: batch.batchId,
                      opened: true,
                      note: notes[batch.batchId] ?? batch.note ?? "",
                      writeOffQuantity: null,
                    })}
                  >
                    Вскрыта
                  </button>
                  <button
                    className="btn"
                    type="button"
                    onClick={() => markBatch.mutate({
                      batchId: batch.batchId,
                      opened: Boolean(batch.openedAt),
                      note: notes[batch.batchId] ?? batch.note ?? "",
                      writeOffQuantity: Number(writeOff[batch.batchId] || 0) || null,
                    })}
                  >
                    Сохранить
                  </button>
                </div>
              </article>
            ))}
          </div>
        </Panel>
      </div>
    </>
  );
}
