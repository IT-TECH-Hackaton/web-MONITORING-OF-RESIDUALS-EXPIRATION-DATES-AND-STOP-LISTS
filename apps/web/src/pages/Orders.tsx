import { ApiError, formatQty, type Order } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Fragment, useState } from "react";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, Panel } from "../ui";

const verdictLabel: Record<string, string> = {
  Ready: "Готово",
  Waste: "Брак",
  Problem: "Отказ",
  Open: "На кухне",
  Rejected: "Не дошёл",
};

function verdict(order: Order): keyof typeof verdictLabel {
  if (order.status === "Rejected") return "Rejected";
  const marks = new Set(order.items.map((item) => item.kitchenStatus));
  if (marks.has("Problem")) return "Problem";
  if (marks.has("Waste")) return "Waste";
  if (order.items.length > 0 && order.items.every((item) => item.kitchenStatus === "Ready")) return "Ready";
  return "Open";
}

function noteOf(order: Order) {
  return order.items.map((item) => item.kitchenNote).find((note) => note) ?? order.rejectionReason;
}

function pillClass(kind: string) {
  if (kind === "Ready") return "pill pill-accepted";
  if (kind === "Waste" || kind === "Problem" || kind === "Rejected") return "pill pill-rejected";
  return "pill";
}

export function OrdersPage() {
  return <OrderJournal mode="manager" />;
}

export function KitchenOrdersPage() {
  return <OrderJournal mode="kitchen" />;
}

function OrderJournal({ mode }: { mode: "manager" | "kitchen" }) {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => client.orders() });
  const [openId, setOpenId] = useState<string | null>(null);
  const [rejectFor, setRejectFor] = useState<string | null>(null);
  const [rejectNote, setRejectNote] = useState("");
  const decide = useMutation({
    mutationFn: (input: { id: string; result: "Ready" | "Waste" | "Problem"; note?: string }) =>
      client.decideOrder(input.id, input.result, input.note),
    onSuccess: async () => {
      setRejectFor(null);
      setRejectNote("");
      await queryClient.invalidateQueries({ queryKey: ["orders"] });
    },
  });

  const rows = (orders.data ?? []).filter((order) => mode === "manager" || order.status === "Accepted");

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Заказы</h1>
          <p>
            {mode === "kitchen"
              ? "Заказы зала. Нажми строку — сколько какого продукта списалось со склада. Брак и отказ оставляют это списание."
              : "Журнал зала: состав, списание со склада, готово, брак или отказ с заметкой."}
          </p>
        </div>
      </div>
      <Panel title={mode === "kitchen" ? "С зала" : "Журнал"}>
        {orders.isLoading ? <p>Загрузка…</p> : null}
        {orders.error ? <ErrorText error={orders.error} /> : null}
        {orders.data && rows.length === 0 ? <Empty>{mode === "kitchen" ? "С зала пока ничего не пришло." : "Заказов ещё не было."}</Empty> : null}
        {rows.length > 0 ? (
          <table>
            <thead>
              <tr>
                <th>Время</th>
                <th>Стол</th>
                <th>Кто</th>
                <th>Состав</th>
                <th>Итог</th>
                <th>Заметка</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((order) => {
                const kind = verdict(order);
                const open = openId === order.id;
                return (
                  <Fragment key={order.id}>
                    <tr className={open ? "order-hit is-open" : "order-hit"}>
                      <td>{formatWhen(order.createdAt)}</td>
                      <td>{order.tableNumber ?? "—"}</td>
                      <td>{order.createdByLogin}</td>
                      <td>
                        <button type="button" className="text-btn" onClick={() => setOpenId(open ? null : order.id)}>
                          {order.items.map((item) => `${item.dishName} × ${item.portions}`).join(", ") || "—"}
                        </button>
                      </td>
                      <td><span className={pillClass(kind)}>{verdictLabel[kind]}</span></td>
                      <td className="reason">{noteOf(order) ?? "—"}</td>
                    </tr>
                    {open ? (
                      <tr key={`${order.id}-detail`} className="order-detail">
                        <td colSpan={6}>
                          <OrderDetail
                            order={order}
                            kind={kind}
                            kitchen={mode === "kitchen"}
                            pending={decide.isPending}
                            error={decide.error}
                            rejecting={rejectFor === order.id}
                            rejectNote={rejectNote}
                            onRejectNote={setRejectNote}
                            onReady={() => decide.mutate({ id: order.id, result: "Ready" })}
                            onWaste={() => decide.mutate({ id: order.id, result: "Waste" })}
                            onRejectStart={() => { setRejectFor(order.id); setRejectNote(""); }}
                            onRejectCancel={() => setRejectFor(null)}
                            onReject={() => decide.mutate({ id: order.id, result: "Problem", note: rejectNote.trim() })}
                          />
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        ) : null}
      </Panel>
    </>
  );
}

function OrderDetail({
  order,
  kind,
  kitchen,
  pending,
  error,
  rejecting,
  rejectNote,
  onRejectNote,
  onReady,
  onWaste,
  onRejectStart,
  onRejectCancel,
  onReject,
}: {
  order: Order;
  kind: string;
  kitchen: boolean;
  pending: boolean;
  error: unknown;
  rejecting: boolean;
  rejectNote: string;
  onRejectNote: (value: string) => void;
  onReady: () => void;
  onWaste: () => void;
  onRejectStart: () => void;
  onRejectCancel: () => void;
  onReject: () => void;
}) {
  const lines = order.writeOffs ?? [];
  return (
    <div className="stack">
      <p className="reason">Со склада по этому заказу</p>
      {lines.length === 0 ? <p>Списания нет: заказ не дошёл до кухни или склад не трогали.</p> : (
        <ul className="writeoff-list">
          {lines.map((line, index) => (
            <li key={`${line.productName}-${index}`}>
              <strong>{line.productName}</strong>
              {" "}
              {formatQty(line.quantity, line.unit ?? "G")}
              <span className="reason"> · было {formatQty(line.before, line.unit ?? "G")}, осталось {formatQty(line.after, line.unit ?? "G")}</span>
            </li>
          ))}
        </ul>
      )}
      {kitchen && kind === "Open" ? (
        <div className="stack">
          <div className="btn-row">
            <button type="button" className="btn" disabled={pending} onClick={onReady}>Готово</button>
            <button type="button" className="btn-ghost" disabled={pending} onClick={onWaste}>Брак</button>
            <button type="button" className="btn-ghost" disabled={pending} onClick={onRejectStart}>Отказ</button>
          </div>
          {rejecting ? (
            <label>
              Заметка к отказу
              <input value={rejectNote} onChange={(event) => onRejectNote(event.target.value)} placeholder="Нет соуса, скажи гостю" required />
            </label>
          ) : null}
          {rejecting ? (
            <div className="btn-row">
              <button type="button" className="btn" disabled={pending || rejectNote.trim().length < 2} onClick={onReject}>
                {pending ? "Отмечаем…" : "Отказать и позвать официанта"}
              </button>
              <button type="button" className="btn-ghost" onClick={onRejectCancel}>Отмена</button>
            </div>
          ) : null}
          {error ? <p className="error-text">{error instanceof ApiError ? error.message : "Не удалось отметить"}</p> : null}
          <p className="hint">Брак и отказ не списывают склад второй раз: продукт уже ушёл, когда зал отправил заказ. Отказ показывает официанту номер стола.</p>
        </div>
      ) : null}
    </div>
  );
}
