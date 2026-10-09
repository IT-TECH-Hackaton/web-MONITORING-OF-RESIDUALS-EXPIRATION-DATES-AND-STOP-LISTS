import { useEffect, useRef, useState } from "react";
import QRCode from "qrcode";
import { useSession } from "../session";

export function PurchaseButton({ productIds, label = "В заявку" }: { productIds?: string[]; label?: string }) {
  const { client } = useSession();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [payload, setPayload] = useState<string | null>(null);
  const [number, setNumber] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    if (!payload || !canvasRef.current) return;
    void QRCode.toCanvas(canvasRef.current, payload, { width: 220, margin: 1 });
  }, [payload]);

  async function requestDraft() {
    setPending(true);
    setError(null);
    try {
      const draft = await client.purchaseRequest(productIds);
      setPayload(draft.payload);
      setNumber(draft.number);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Заявку не удалось собрать");
    } finally {
      setPending(false);
    }
  }

  return (
    <>
      <button className="btn-ghost" type="button" disabled={pending} onClick={() => void requestDraft()}>
        {pending ? "Собираем…" : label}
      </button>
      {error ? <p className="error-text">{error}</p> : null}
      {payload ? (
        <div className="modal-back" onClick={() => setPayload(null)}>
          <div className="modal" onClick={(event) => event.stopPropagation()}>
            <strong>Заявка {number}</strong>
            <p className="reason">Склад не меняется. Это код для поставщика. Партии появятся после приёмки с живым сертификатом.</p>
            <canvas ref={canvasRef} />
            <button className="btn" type="button" onClick={() => setPayload(null)}>Закрыть</button>
          </div>
        </div>
      ) : null}
    </>
  );
}

export function forecastBasis(ordersThisWeek: number) {
  const base = `С понедельника принято заказов: ${ordersThisWeek}.`;
  return ordersThisWeek < 5 ? `${base} Темп ещё ненадёжный.` : base;
}
