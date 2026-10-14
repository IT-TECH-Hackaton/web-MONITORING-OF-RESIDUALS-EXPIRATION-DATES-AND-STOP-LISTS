import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useSession } from "../session";

export function PurchaseButton({ productIds, label = "В заказ поставщику" }: { productIds?: string[]; label?: string }) {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function addToOrder() {
    setPending(true);
    setError(null);
    setOk(null);
    try {
      const result = await client.addForecastToOrder(productIds);
      setOk(`В заказ: ${result.count} позиц.`);
      await queryClient.invalidateQueries({ queryKey: ["purchase-lines"] });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "В заказ не попало");
    } finally {
      setPending(false);
    }
  }

  return (
    <>
      <button className="btn-ghost" type="button" disabled={pending} onClick={() => void addToOrder()}>
        {pending ? "Кладём…" : label}
      </button>
      {error ? <p className="error-text">{error}</p> : null}
      {ok ? <p className="hint">{ok}</p> : null}
    </>
  );
}

export function forecastBasis(ordersThisWeek: number) {
  const base = `С понедельника принято заказов: ${ordersThisWeek}.`;
  return ordersThisWeek < 5 ? `${base} Темп ещё ненадёжный.` : base;
}
