import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, FreshnessPill, Hours, Panel, Qty } from "../ui";

export function StockPage() {
  const { client } = useSession();
  const stock = useQuery({ queryKey: ["stock"], queryFn: () => client.stock() });
  const [query, setQuery] = useState("");
  const visible = (stock.data ?? []).filter((item) => item.name.toLocaleLowerCase("ru").includes(query.trim().toLocaleLowerCase("ru")));
  return (
    <>
      <div className="page-title">
        <div>
          <h1>Склад</h1>
          <p>Доступный остаток не включает просроченные партии.</p>
        </div>
      </div>
      <Panel title="Продукты" action={<input placeholder="Найти продукт" value={query} onChange={(event) => setQuery(event.target.value)} />}>
        {stock.isLoading ? <p>Загрузка…</p> : null}
        {stock.error ? <ErrorText error={stock.error} /> : null}
        {stock.data?.length === 0 ? <Empty>Каталог пуст. Примите первую поставку.</Empty> : null}
        {stock.data && stock.data.length > 0 && visible.length === 0 ? <Empty>Ничего не найдено.</Empty> : null}
        {visible.length > 0 ? (
          <table>
            <thead><tr><th>Продукт</th><th>Доступно</th><th>Ближайший срок</th><th>Осталось</th><th></th></tr></thead>
            <tbody>
              {visible.map((item) => (
                <tr key={item.productId} className="clickable">
                  <td><Link to={`/stock/${item.productId}`}>{item.name}</Link></td>
                  <td><Qty value={item.available} unit={item.unit} /></td>
                  <td>{item.nearestExpiry ? formatWhen(item.nearestExpiry) : "—"}</td>
                  <td><Hours value={item.hoursLeft} /></td>
                  <td><FreshnessPill freshness={item.freshness} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </Panel>
    </>
  );
}

export function StockProductPage() {
  const { productId = "" } = useParams();
  const { client } = useSession();
  const batches = useQuery({ queryKey: ["batches", productId], queryFn: () => client.batches(productId), enabled: Boolean(productId) });
  const name = batches.data?.[0]?.productName ?? "Партии";
  return (
    <>
      <div className="page-title">
        <div>
          <h1>{name}</h1>
          <p>Списание идёт с партии, у которой срок кончается раньше.</p>
        </div>
        <Link to="/stock">Весь склад</Link>
      </div>
      <Panel title="Партии">
        {batches.isLoading ? <p>Загрузка…</p> : null}
        {batches.error ? <ErrorText error={batches.error} /> : null}
        {batches.data?.length === 0 ? <Empty>Партий нет.</Empty> : null}
        {batches.data && batches.data.length > 0 ? (
          <table>
            <thead><tr><th>Поставка</th><th>Пришло</th><th>Остаток</th><th>Годен до</th><th>Осталось</th><th>Статус</th></tr></thead>
            <tbody>
              {batches.data.map((batch) => (
                <tr key={batch.id}>
                  <td><Link to={`/deliveries/${batch.deliveryId}`}>{batch.deliveryNumber}</Link></td>
                  <td><Qty value={batch.quantityInitial} unit={batch.unit} /></td>
                  <td><Qty value={batch.quantityRemaining} unit={batch.unit} /></td>
                  <td>{formatWhen(batch.expiresAt)}</td>
                  <td><Hours value={batch.hoursLeft} /></td>
                  <td><FreshnessPill freshness={batch.freshness} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </Panel>
    </>
  );
}
