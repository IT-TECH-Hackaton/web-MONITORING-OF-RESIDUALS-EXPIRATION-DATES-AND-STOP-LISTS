import { formatQty, type Batch, type Freshness, type Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, formatExpiry, formatWhen, FreshnessPill, Hours, Panel, Qty } from "../ui";
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

function receivedQty(batch: Batch) {
  if (batch.acceptedQuantity === 0 || batch.status === "Arrived") return 0;
  return batch.acceptedQuantity ?? batch.quantityInitial;
}

export function StockPage() {
  const { client } = useSession();
  const navigate = useNavigate();
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
          <p>Нажмите продукт — откроется карточка: какими партиями пришло, сколько и до какого числа.</p>
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
              <thead><tr><th>Продукт</th><th className="num">Доступно</th><th className="num">Цена закупки</th><th>Ближайший срок</th><th>Осталось</th><th></th><th></th></tr></thead>
              <tbody>
                {visible.map((item) => (
                  <tr key={item.productId} className="stock-row" onClick={() => navigate(`/stock/${item.productId}`)}>
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
                    <td className={item.nearestExpiry ? undefined : "is-zero"}>{item.nearestExpiry ? formatExpiry(item.nearestExpiry) : "—"}</td>
                    <td><Hours value={item.hoursLeft} /></td>
                    <td><FreshnessPill freshness={item.freshness} /></td>
                    <td>
                      <Link to={`/stock/${item.productId}`} className="btn-ghost stock-open" onClick={(event) => event.stopPropagation()}>Партии</Link>
                    </td>
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
  const uses = useQuery({ queryKey: ["uses", productId], queryFn: () => client.productUses(productId), enabled: Boolean(productId) });
  const stock = useQuery({ queryKey: ["stock"], queryFn: () => client.stock() });
  const item = stock.data?.find((row) => row.productId === productId);
  const arrived = (batches.data ?? []).filter((batch) => receivedQty(batch) > 0);
  const waiting = (batches.data ?? []).filter((batch) => batch.status === "Arrived");
  const name = item?.name ?? batches.data?.[0]?.productName ?? "Продукт";
  const unit = item?.unit ?? batches.data?.[0]?.unit;
  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>{name}</h1>
          <p>
            {item && unit ? <>Сейчас доступно <Qty value={item.available} unit={unit} />. </> : null}
            Партии, которые уже на складе: сколько пришло и до какого числа годно. Раньше кончается срок — раньше уходит в готовку.
          </p>
        </div>
        <Link to="/stock" className="btn-ghost">Весь склад</Link>
      </header>
      <Panel title="Партии">
        {batches.isLoading ? <p>Загрузка…</p> : null}
        {batches.error ? <ErrorText error={batches.error} /> : null}
        {!batches.isLoading && arrived.length === 0 ? <Empty>На склад ещё ничего не пришло.</Empty> : null}
        {arrived.length > 0 ? (
          <div className="rep-scroll">
            <table className="rep-table rep-staff">
              <thead><tr><th>Поставка</th><th className="num">Пришло</th><th className="num">Осталось</th><th>Годен до</th><th></th></tr></thead>
              <tbody>
                {arrived.map((batch) => {
                  const came = receivedQty(batch);
                  const share = came <= 0 ? 0 : Math.max(4, (batch.quantityRemaining / came) * 100);
                  return (
                    <tr key={batch.id}>
                      <td>
                        <Link to={`/deliveries/${batch.deliveryId}`} className="rep-person">
                          <span className="rep-dot" style={{ background: freshnessColor[batch.freshness] }} />
                          <span>{batch.deliveryNumber}</span>
                        </Link>
                      </td>
                      <td className="num"><Qty value={came} unit={batch.unit} /></td>
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
                      <td>{formatExpiry(batch.expiresAt)}</td>
                      <td><FreshnessPill freshness={batch.freshness} /></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : null}
        {waiting.length > 0 ? (
          <p className="reason">Ещё не на складе: {waiting.map((batch) => `${batch.deliveryNumber}, заявлено ${batch.quantityInitial}`).join("; ")}. Срок станет складским после итога менеджера.</p>
        ) : null}
      </Panel>
      <Panel title="Куда уходило">
        <p className="reason">Когда и куда: приход с поставки, стол заказа или акт. У заказа видно, сколько было и сколько осталось.</p>
        {uses.isLoading ? <p>Загрузка…</p> : null}
        {uses.error ? <ErrorText error={uses.error} /> : null}
        {!uses.isLoading && (uses.data?.length ?? 0) === 0 ? <Empty>Движений пока нет.</Empty> : null}
        {uses.data && uses.data.length > 0 ? (
          <div className="rep-scroll">
            <table className="rep-table rep-staff">
              <thead><tr><th>Когда</th><th>Куда</th><th className="num">Сколько</th></tr></thead>
              <tbody>
                {uses.data.map((row) => (
                  <tr key={row.id}>
                    <td>{formatWhen(row.at)}</td>
                    <td>{row.place}</td>
                    <td className="num">
                      {row.before != null && row.after != null
                        ? <>{formatQty(row.before, row.unit)} − {formatQty(row.quantity, row.unit)} = {formatQty(row.after, row.unit)}</>
                        : <Qty value={row.quantity} unit={row.unit} />}
                    </td>
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
