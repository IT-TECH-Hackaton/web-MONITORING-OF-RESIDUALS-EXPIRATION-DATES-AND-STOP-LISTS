import { newId, type Delivery, type Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, FreshnessPill, Panel, Qty } from "../ui";

type Line = { key: string; productId: string; name: string; unit: Unit; quantity: string; shelfLifeHours: string };

const emptyLine = (): Line => ({ key: newId(), productId: "", name: "", unit: "G", quantity: "", shelfLifeHours: "48" });

function nearestBatch(delivery: Delivery) {
  return delivery.batches.slice().sort((a, b) => a.hoursLeft - b.hoursLeft)[0];
}

function productChips(delivery: Delivery) {
  const names = [...new Set(delivery.batches.map((batch) => batch.productName))];
  const shown = names.slice(0, 4);
  const rest = names.length - shown.length;
  return { shown, rest };
}

export function DeliveriesPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const deliveries = useQuery({ queryKey: ["deliveries"], queryFn: () => client.deliveries() });
  const products = useQuery({ queryKey: ["products"], queryFn: () => client.products() });
  const [number, setNumber] = useState("");
  const [lines, setLines] = useState<Line[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: () => client.createDelivery({
      id: newId(),
      number,
      items: lines.map((line) => ({
        productId: line.productId || newId(),
        name: line.name,
        unit: line.unit,
        quantity: Number(line.quantity),
        shelfLifeHours: Number(line.shelfLifeHours),
      })),
    }),
    onSuccess: async () => {
      setNumber("");
      setLines([emptyLine()]);
      setError(null);
      await queryClient.invalidateQueries();
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Поставка не принята"),
  });

  function update(key: string, patch: Partial<Line>) {
    setLines((current) => current.map((line) => line.key === key ? { ...line, ...patch } : line));
  }

  const rows = deliveries.data ?? [];
  const maxBatches = Math.max(1, ...rows.map((delivery) => delivery.batches.length));

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Поставки</h1>
          <p>Каждая позиция становится партией со своим сроком годности.</p>
        </div>
        <Link to="/intake" className="btn-ghost">Приёмка QR</Link>
      </header>
      <Panel title="Новая поставка">
          <form className="form" onSubmit={(event) => { event.preventDefault(); create.mutate(); }}>
            <label className="del-number">Номер<input value={number} onChange={(event) => setNumber(event.target.value)} required /></label>
            {lines.map((line) => (
              <div className="line" key={line.key}>
                <label>
                  Продукт
                  <input
                    list="product-names"
                    value={line.name}
                    onChange={(event) => {
                      const product = products.data?.find((item) => item.name === event.target.value);
                      update(line.key, { name: event.target.value, productId: product?.id ?? "", unit: product?.unit ?? line.unit });
                    }}
                    required
                  />
                </label>
                <label>
                  Ед.
                  <select value={line.unit} onChange={(event) => update(line.key, { unit: event.target.value as Unit })}>
                    <option value="G">г</option>
                    <option value="Ml">мл</option>
                    <option value="Pcs">шт</option>
                  </select>
                </label>
                <label>Кол-во<input type="number" min="0.001" step="0.001" value={line.quantity} onChange={(event) => update(line.key, { quantity: event.target.value })} required /></label>
                <label>Часы хранения<input type="number" min="1" step="1" value={line.shelfLifeHours} onChange={(event) => update(line.key, { shelfLifeHours: event.target.value })} required /></label>
              </div>
            ))}
            <datalist id="product-names">
              {products.data?.map((product) => <option key={product.id} value={product.name} />)}
            </datalist>
            {error ? <p className="error-text">{error}</p> : null}
            <div className="btn-row">
              <button className="btn" disabled={create.isPending}>Принять</button>
              <button className="btn-ghost" type="button" onClick={() => setLines((current) => [...current, emptyLine()])}>Ещё позиция</button>
            </div>
          </form>
        </Panel>
      <Panel title="Журнал">
        {deliveries.isLoading ? <p>Загрузка…</p> : null}
        {deliveries.error ? <ErrorText error={deliveries.error} /> : null}
        {rows.length === 0 && !deliveries.isLoading ? <Empty>Поставок пока нет.</Empty> : null}
        {rows.length > 0 ? (
          <div className="rep-scroll">
            <table className="rep-table rep-staff">
              <thead><tr><th>Номер</th><th>Дата</th><th>Состав</th><th className="num">Партий</th><th>Ближайший срок</th></tr></thead>
              <tbody>
                {rows.map((delivery) => {
                  const chips = productChips(delivery);
                  const nearest = nearestBatch(delivery);
                  return (
                    <tr key={delivery.id}>
                      <td>
                        <Link to={`/deliveries/${delivery.id}`} className="rep-person">
                          <span className="rep-dot" style={{ background: "var(--accent)" }} />
                          <span>{delivery.number}</span>
                        </Link>
                      </td>
                      <td>{formatWhen(delivery.createdAt)}</td>
                      <td>
                        <div className="del-chips">
                          {chips.shown.map((name) => <span key={name}>{name}</span>)}
                          {chips.rest > 0 ? <span>+{chips.rest}</span> : null}
                        </div>
                      </td>
                      <td className="num">
                        <div className="rep-money">
                          <strong>{delivery.batches.length}</strong>
                          <span className="rep-track" aria-hidden="true">
                            <span className="is-rose" style={{ width: `${Math.max(8, (delivery.batches.length / maxBatches) * 100)}%` }} />
                          </span>
                        </div>
                      </td>
                      <td>{nearest ? <FreshnessPill freshness={nearest.freshness} /> : "—"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : null}
      </Panel>
    </div>
  );
}

export function DeliveryPage() {
  const { id = "" } = useParams();
  const { client } = useSession();
  const delivery = useQuery({ queryKey: ["deliveries", id], queryFn: () => client.delivery(id), enabled: Boolean(id) });
  if (delivery.isLoading) return <p>Загрузка поставки…</p>;
  if (delivery.error) return <ErrorText error={delivery.error} />;
  const data = delivery.data!;
  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>{data.number}</h1>
          <p>{formatWhen(data.createdAt)}{data.supplier ? ` · ${data.supplier}` : ""} · партий {data.batches.length}</p>
        </div>
        <Link to="/deliveries" className="btn-ghost">Все поставки</Link>
      </header>
      <Panel title="Партии">
        <div className="rep-scroll">
          <table className="rep-table rep-staff">
            <thead><tr><th>Продукт</th><th className="num">Пришло</th><th className="num">Остаток</th><th>Годен до</th><th>Сертификат</th><th></th></tr></thead>
            <tbody>
              {data.batches.map((batch) => {
                const share = batch.quantityInitial <= 0 ? 0 : Math.max(4, (batch.quantityRemaining / batch.quantityInitial) * 100);
                return (
                  <tr key={batch.id}>
                    <td>
                      <Link to={`/stock/${batch.productId}`} className="rep-person">
                        <span className="rep-dot" style={{ background: "var(--amber)" }} />
                        <span>{batch.productName}</span>
                      </Link>
                    </td>
                    <td className="num"><Qty value={batch.quantityInitial} unit={batch.unit} /></td>
                    <td className="num">
                      <div className="rep-money">
                        <strong><Qty value={batch.quantityRemaining} unit={batch.unit} /></strong>
                        <span className="rep-track" aria-hidden="true"><span className="is-amber" style={{ width: `${share}%` }} /></span>
                      </div>
                    </td>
                    <td>{formatWhen(batch.expiresAt)}</td>
                    <td className="reason">
                      {batch.certificateNumber
                        ? `${batch.certificateNumber}, ${batch.certificateIssuer ?? ""} до ${batch.certificateExpiresAt ? formatWhen(batch.certificateExpiresAt) : "—"}`
                        : "без QR-сверки"}
                    </td>
                    <td><FreshnessPill freshness={batch.freshness} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </Panel>
    </div>
  );
}
