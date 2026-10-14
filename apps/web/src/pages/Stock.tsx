import type { Freshness, Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, FreshnessPill, Hours, Panel, Qty } from "../ui";
import { ExpiredWriteOffTable } from "./Dashboard";

const freshnessColor: Record<Freshness, string> = {
  Ok: "var(--sage)",
  Expiring: "var(--amber)",
  Expired: "var(--accent)",
  Empty: "var(--ink-soft)",
};

function rub(value: number) {
  return new Intl.NumberFormat("ru-RU", { style: "currency", currency: "RUB", maximumFractionDigits: 0 }).format(value);
}

function purchaseLabel(price: number, unit: Unit) {
  if (unit === "G") return `${rub(price * 1000)} / кг`;
  if (unit === "Ml") return `${rub(price * 1000)} / л`;
  return `${rub(price)} / шт`;
}

export function StockPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const stock = useQuery({ queryKey: ["stock"], queryFn: () => client.stock() });
  const dashboard = useQuery({ queryKey: ["dashboard"], queryFn: () => client.dashboard() });
  const expired = dashboard.data?.expired ?? [];
  const [query, setQuery] = useState("");
  const [name, setName] = useState("");
  const [unit, setUnit] = useState<Unit>("G");
  const [error, setError] = useState<string | null>(null);
  const visible = (stock.data ?? []).filter((item) => item.name.toLocaleLowerCase("ru").includes(query.trim().toLocaleLowerCase("ru")));
  const maxAvailable = Math.max(1, ...visible.map((item) => item.available));
  const create = useMutation({
    mutationFn: () => client.createProduct(name.trim(), unit),
    onSuccess: async () => {
      setName("");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["stock"] });
      await queryClient.invalidateQueries({ queryKey: ["products"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Продукт не создан"),
  });
  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Склад</h1>
          <p>Холодильник заведения. Карточку продукта заводят до прихода машины.</p>
        </div>
      </header>
      <Panel title="Новый продукт">
        <p className="reason">Имя и единица. Остаток появится после приёмки, не сейчас.</p>
        <form className="form" onSubmit={(event) => { event.preventDefault(); create.mutate(); }}>
          <label>Название<input value={name} onChange={(event) => setName(event.target.value)} required placeholder="Лосось" /></label>
          <label>
            Ед.
            <select value={unit} onChange={(event) => setUnit(event.target.value as Unit)}>
              <option value="G">г</option>
              <option value="Ml">мл</option>
              <option value="Pcs">шт</option>
            </select>
          </label>
          {error ? <p className="error-text">{error}</p> : null}
          <button className="btn" disabled={create.isPending}>В каталог</button>
        </form>
      </Panel>
      {expired.length > 0 ? (
        <Panel title="Снято по сроку">
          <p className="reason">Этих партий в «доступно» уже нет. Фон списал, когда вышел «годен до».</p>
          <ExpiredWriteOffTable rows={expired} />
        </Panel>
      ) : null}
      <Panel title="Продукты" action={<input placeholder="Найти продукт" value={query} onChange={(event) => setQuery(event.target.value)} />}>
        {stock.isLoading ? <p>Загрузка…</p> : null}
        {stock.error ? <ErrorText error={stock.error} /> : null}
        {stock.data?.length === 0 ? <Empty>Каталог пуст. Примите первую поставку.</Empty> : null}
        {stock.data && stock.data.length > 0 && visible.length === 0 ? <Empty>Ничего не найдено.</Empty> : null}
        {visible.length > 0 ? (
          <div className="rep-scroll">
            <table className="rep-table rep-staff">
              <thead><tr><th>Продукт</th><th className="num">Доступно</th><th className="num">Цена закупки</th><th>Ближайший срок</th><th>Осталось</th><th></th></tr></thead>
              <tbody>
                {visible.map((item) => (
                  <tr key={item.productId}>
                    <td>
                      <Link to={`/stock/${item.productId}`} className="rep-person">
                        <span className="rep-dot" style={{ background: freshnessColor[item.freshness] }} />
                        <span>{item.name}</span>
                      </Link>
                    </td>
                    <td className="num">
                      <div className="rep-money">
                        <strong className={item.available === 0 ? "is-zero" : undefined}><Qty value={item.available} unit={item.unit} /></strong>
                        {item.available > 0 ? (
                          <span className="rep-track" aria-hidden="true">
                            <span style={{ width: `${Math.max(6, (item.available / maxAvailable) * 100)}%`, background: freshnessColor[item.freshness] }} />
                          </span>
                        ) : null}
                      </div>
                    </td>
                    <td className={item.purchasePrice === 0 ? "num is-zero" : "num"}>{purchaseLabel(item.purchasePrice, item.unit)}</td>
                    <td className={item.nearestExpiry ? undefined : "is-zero"}>{item.nearestExpiry ? formatWhen(item.nearestExpiry) : "—"}</td>
                    <td><Hours value={item.hoursLeft} /></td>
                    <td><FreshnessPill freshness={item.freshness} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </Panel>
    </div>
  );
}

export function StockProductPage() {
  const { productId = "" } = useParams();
  const { client } = useSession();
  const batches = useQuery({ queryKey: ["batches", productId], queryFn: () => client.batches(productId), enabled: Boolean(productId) });
  const name = batches.data?.[0]?.productName ?? "Партии";
  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>{name}</h1>
          <p>Списание идёт с партии, у которой срок кончается раньше.</p>
        </div>
        <Link to="/stock" className="btn-ghost">Весь склад</Link>
      </header>
      <Panel title="Партии">
        {batches.isLoading ? <p>Загрузка…</p> : null}
        {batches.error ? <ErrorText error={batches.error} /> : null}
        {batches.data?.length === 0 ? <Empty>Партий нет.</Empty> : null}
        {batches.data && batches.data.length > 0 ? (
          <div className="rep-scroll">
            <table className="rep-table rep-staff">
              <thead><tr><th>Поставка</th><th className="num">Пришло</th><th className="num">Остаток</th><th>Годен до</th><th>Осталось</th><th>Статус</th></tr></thead>
              <tbody>
                {batches.data.map((batch) => {
                  const share = batch.quantityInitial <= 0 ? 0 : Math.max(4, (batch.quantityRemaining / batch.quantityInitial) * 100);
                  return (
                    <tr key={batch.id}>
                      <td>
                        <Link to={`/deliveries/${batch.deliveryId}`} className="rep-person">
                          <span className="rep-dot" style={{ background: freshnessColor[batch.freshness] }} />
                          <span>{batch.deliveryNumber}</span>
                        </Link>
                      </td>
                      <td className="num"><Qty value={batch.quantityInitial} unit={batch.unit} /></td>
                      <td className="num">
                        <div className="rep-money">
                          <strong className={batch.quantityRemaining === 0 ? "is-zero" : undefined}><Qty value={batch.quantityRemaining} unit={batch.unit} /></strong>
                          {batch.quantityRemaining > 0 ? (
                            <span className="rep-track" aria-hidden="true">
                              <span style={{ width: `${share}%`, background: freshnessColor[batch.freshness] }} />
                            </span>
                          ) : null}
                        </div>
                      </td>
                      <td>{formatWhen(batch.expiresAt)}</td>
                      <td><Hours value={batch.hoursLeft} /></td>
                      <td><FreshnessPill freshness={batch.freshness} /></td>
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
