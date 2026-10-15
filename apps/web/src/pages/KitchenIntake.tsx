import { unitLabel, type Batch, type Delivery } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useSession } from "../session";
import { Empty, ErrorText, Panel, Qty, expiryInputValue } from "../ui";

const skipReasons = ["Не подошло", "Порча", "Короткий срок", "Не привезли"];

type LineDraft = { quantity?: string; day?: string; note?: string };

function quantityOf(draft: Record<string, LineDraft>, batch: Batch) {
  return draft[batch.id]?.quantity ?? String(batch.acceptedQuantity ?? batch.quantityInitial);
}

function packDay(iso: string) {
  const day = expiryInputValue(iso);
  return day.startsWith("2099") || day.startsWith("2090") ? "" : day;
}

function dayOf(draft: Record<string, LineDraft>, batch: Batch) {
  if (draft[batch.id]?.day !== undefined) return draft[batch.id]!.day!;
  return packDay(batch.expiresAt);
}

function noteOf(draft: Record<string, LineDraft>, batch: Batch) {
  return draft[batch.id]?.note ?? (batch.acceptedQuantity === 0 ? batch.kitchenNote ?? skipReasons[0] : "");
}

export function KitchenIntakePage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const intake = useQuery({ queryKey: ["intake"], queryFn: () => client.intakeDeliveries(), refetchInterval: 5000 });
  const [draft, setDraft] = useState<Record<string, LineDraft>>({});
  const [error, setError] = useState<string | null>(null);
  const confirm = useMutation({
    mutationFn: (delivery: Delivery) => client.confirmIntake(delivery.id, delivery.batches.map((batch) => {
      const quantity = Number(quantityOf(draft, batch));
      return {
        batchId: batch.id,
        quantity,
        expiresAt: `${dayOf(draft, batch)}T12:00:00.000Z`,
        note: quantity === 0 ? (noteOf(draft, batch) || skipReasons[0]) : null,
      };
    })),
    onSuccess: async () => {
      setError(null);
      setDraft({});
      await queryClient.invalidateQueries({ queryKey: ["intake"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Приёмка не сохранилась"),
  });
  const cancel = useMutation({
    mutationFn: (id: string) => client.cancelDelivery(id),
    onSuccess: async () => {
      setError(null);
      setDraft({});
      await queryClient.invalidateQueries({ queryKey: ["intake"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Поставку не удалось отменить"),
  });

  function patch(batchId: string, next: LineDraft) {
    setDraft((current) => ({ ...current, [batchId]: { ...current[batchId], ...next } }));
  }

  function skip(batch: Batch) {
    const skipped = Number(quantityOf(draft, batch)) === 0;
    patch(batch.id, skipped
      ? { quantity: String(batch.quantityInitial), note: "" }
      : { quantity: "0", note: noteOf(draft, batch) || skipReasons[0] });
  }

  function submit(delivery: Delivery) {
    for (const batch of delivery.batches) {
      const quantity = Number(quantityOf(draft, batch));
      if (!Number.isFinite(quantity) || quantity < 0 || quantity > batch.quantityInitial) {
        setError(`«${batch.productName}»: от 0 до заявленного.`);
        return;
      }
      if (!dayOf(draft, batch)) {
        setError(`«${batch.productName}»: укажите срок с пачки.`);
        return;
      }
    }
    setError(null);
    confirm.mutate(delivery);
  }

  const rows = intake.data ?? [];

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Приёмка</h1>
          <p>Срок поставщика уже стоит — это пример. Поправьте его по пачке. Возьмите часть или нажмите «Не берём», если позиция не подошла. На склад это попадёт после итога менеджера.</p>
        </div>
      </header>
      {intake.isLoading ? <p>Загрузка…</p> : null}
      {intake.error ? <ErrorText error={intake.error} /> : null}
      {rows.length === 0 && !intake.isLoading ? <Empty>Сейчас нечего принимать.</Empty> : null}
      {rows.map((delivery) => (
        <Panel key={delivery.id} title={delivery.number}>
          <p className="reason">{delivery.supplier ?? "Без поставщика"}{delivery.stage === "KitchenDone" ? " · ждёт итог менеджера, можно поправить" : ""}</p>
          <div className="intake-lines">
            {delivery.batches.map((batch) => {
              const quantity = quantityOf(draft, batch);
              const skipped = Number(quantity) === 0;
              return (
                <article key={batch.id} className={skipped ? "intake-line is-skip" : "intake-line"}>
                  <div className="intake-name">
                    <strong>{batch.productName}</strong>
                    <span className="reason">заявлено <Qty value={batch.quantityInitial} unit={batch.unit} /> · срок с поставки {packDay(batch.expiresAt) || "не указан"}</span>
                  </div>
                  <label className="intake-field">
                    Годен до
                    <input type="date" value={dayOf(draft, batch)} onChange={(event) => patch(batch.id, { day: event.target.value })} required />
                  </label>
                  <label className="intake-field">
                    Взяли
                    <span className="intake-take">
                      <input
                        type="number"
                        min="0"
                        max={batch.quantityInitial}
                        step="0.001"
                        value={quantity}
                        disabled={skipped}
                        onChange={(event) => patch(batch.id, { quantity: event.target.value })}
                      />
                      <span>{unitLabel(batch.unit)}</span>
                    </span>
                  </label>
                  <button type="button" className="btn-ghost intake-skip" onClick={() => skip(batch)}>
                    {skipped ? "Вернуть" : "Не берём"}
                  </button>
                  {skipped ? (
                    <label className="intake-field intake-why">
                      Почему
                      <select value={noteOf(draft, batch) || skipReasons[0]} onChange={(event) => patch(batch.id, { note: event.target.value })}>
                        {skipReasons.map((reason) => <option key={reason}>{reason}</option>)}
                      </select>
                    </label>
                  ) : null}
                </article>
              );
            })}
          </div>
          <div className="btn-row">
            <button type="button" className="btn" disabled={confirm.isPending || cancel.isPending} onClick={() => submit(delivery)}>Подтвердить приёмку</button>
            <button
              type="button"
              className="btn-ghost"
              disabled={confirm.isPending || cancel.isPending}
              onClick={() => {
                if (window.confirm(`Отменить поставку ${delivery.number} целиком? На склад ничего не попадёт.`)) cancel.mutate(delivery.id);
              }}
            >
              Отменить поставку
            </button>
          </div>
        </Panel>
      ))}
      {error ? <p className="error-text">{error}</p> : null}
    </div>
  );
}
