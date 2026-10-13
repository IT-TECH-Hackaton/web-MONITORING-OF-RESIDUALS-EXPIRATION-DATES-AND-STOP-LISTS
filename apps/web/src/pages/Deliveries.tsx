import { newId, type Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, FreshnessPill, Panel, Qty } from "../ui";

type Line = { key: string; productId: string; name: string; unit: Unit; quantity: string; shelfLifeHours: string };

const emptyLine = (): Line => ({ key: newId(), productId: "", name: "", unit: "G", quantity: "", shelfLifeHours: "48" });

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

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Поставки</h1>
          <p>Каждая позиция становится партией со своим сроком годности.</p>
        </div>
        <Link to="/intake">Приёмка QR</Link>
      </div>
      <div className="split">
        <Panel title="Журнал">
          {deliveries.isLoading ? <p>Загрузка…</p> : null}
          {deliveries.error ? <ErrorText error={deliveries.error} /> : null}
          {deliveries.data?.length === 0 ? <Empty>Поставок пока нет.</Empty> : null}
          {deliveries.data && deliveries.data.length > 0 ? (
            <table>
              <thead><tr><th>Номер</th><th>Дата</th><th>Партий</th></tr></thead>
              <tbody>
                {deliveries.data.map((delivery) => (
                  <tr key={delivery.id} className="clickable">
                    <td><Link to={`/deliveries/${delivery.id}`}>{delivery.number}</Link></td>
                    <td>{formatWhen(delivery.createdAt)}</td>
                    <td>{delivery.batches.length}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </Panel>
        <Panel title="Новая поставка">
          <form className="form" onSubmit={(event) => { event.preventDefault(); create.mutate(); }}>
            <label>Номер<input value={number} onChange={(event) => setNumber(event.target.value)} required /></label>
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
      </div>
    </>
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
    <>
      <div className="page-title">
        <div>
          <h1>{data.number}</h1>
          <p>{formatWhen(data.createdAt)}{data.supplier ? ` · ${data.supplier}` : ""}</p>
        </div>
        <Link to="/deliveries">Все поставки</Link>
      </div>
      <Panel title="Партии">
        <table>
          <thead><tr><th>Продукт</th><th>Пришло</th><th>Остаток</th><th>Годен до</th><th>Сертификат</th><th></th></tr></thead>
          <tbody>
            {data.batches.map((batch) => (
              <tr key={batch.id}>
                <td><Link to={`/stock/${batch.productId}`}>{batch.productName}</Link></td>
                <td><Qty value={batch.quantityInitial} unit={batch.unit} /></td>
                <td><Qty value={batch.quantityRemaining} unit={batch.unit} /></td>
                <td>{formatWhen(batch.expiresAt)}</td>
                <td className="reason">
                  {batch.certificateNumber
                    ? `${batch.certificateNumber}, ${batch.certificateIssuer ?? ""} до ${batch.certificateExpiresAt ? formatWhen(batch.certificateExpiresAt) : "—"}`
                    : "без QR-сверки"}
                </td>
                <td><FreshnessPill freshness={batch.freshness} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </>
  );
}
