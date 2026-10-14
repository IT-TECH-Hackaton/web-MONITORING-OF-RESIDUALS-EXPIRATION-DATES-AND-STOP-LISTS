import { formatQty, newId, unitLabel, type Delivery, type PurchaseLine, type PurchaseSource, type Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useSession } from "../session";
import { Empty, ErrorText, formatWhen, FreshnessPill, Panel, Qty } from "../ui";

type Line = { key: string; productId: string; name: string; unit: Unit; quantity: string; expiresAt: string };

function daysFromToday(days: number) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
}

/** Типичный срок с этикетки, пока оператор не поправит дату с пачки. */
function typicalExpiry(name: string) {
  const n = name.toLocaleLowerCase("ru");
  if (/лосось|кревет/.test(n)) return daysFromToday(2);
  if (/курица/.test(n)) return daysFromToday(4);
  if (/говяд/.test(n)) return daysFromToday(5);
  if (/молоко/.test(n)) return daysFromToday(5);
  if (/сливки/.test(n)) return daysFromToday(7);
  if (/моцарел/.test(n)) return daysFromToday(12);
  if (/сливочн/.test(n)) return daysFromToday(21);
  if (/яйц/.test(n)) return daysFromToday(21);
  if (/салат/.test(n)) return daysFromToday(4);
  if (/базилик/.test(n)) return daysFromToday(5);
  if (/томат|огурец/.test(n)) return daysFromToday(6);
  if (/лимон/.test(n)) return daysFromToday(10);
  if (/лук/.test(n)) return daysFromToday(12);
  if (/картоф/.test(n)) return daysFromToday(18);
  if (/чеснок/.test(n)) return daysFromToday(20);
  if (/кофе|мука|сахар|рис|спагет|масло/.test(n)) return daysFromToday(90);
  return daysFromToday(7);
}

const emptyLine = (): Line => ({ key: newId(), productId: "", name: "", unit: "G", quantity: "", expiresAt: typicalExpiry("") });

function requestDate() {
  return new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", year: "numeric" }).format(new Date());
}

function requestText(rows: PurchaseLine[], supplier: string) {
  const who = supplier.trim() ? `\nКому: ${supplier.trim()}` : "";
  const body = rows
    .map((row, index) => `${index + 1}. ${row.productName} — ${formatQty(row.quantity, row.unit)}${row.note ? ` — ${row.note}` : ""}`)
    .join("\n");
  return `Заявка на поставку\n${requestDate()}${who}\n\n${body}`;
}

function csvEscape(value: string) {
  if (/[;"\n]/.test(value)) return `"${value.replaceAll('"', '""')}"`;
  return value;
}

function downloadRequestCsv(rows: PurchaseLine[]) {
  const header = "№;Продукт;Количество;Ед.;Комментарий";
  const lines = rows.map((row, index) =>
    [String(index + 1), row.productName, String(row.quantity).replace(".", ","), unitLabel(row.unit), row.note ?? ""]
      .map(csvEscape)
      .join(";"));
  const blob = new Blob([`\uFEFF${[header, ...lines].join("\n")}`], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `zayavka-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

function sourceLabel(source: PurchaseSource) {
  if (source === "Chef") return "кухня";
  if (source === "Forecast") return "прогноз";
  return "менеджер";
}

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
  const order = useQuery({ queryKey: ["purchase-lines"], queryFn: () => client.purchaseLines(true) });
  const [number, setNumber] = useState("");
  const [supplier, setSupplier] = useState("");
  const [orderProduct, setOrderProduct] = useState("");
  const [orderQty, setOrderQty] = useState("");
  const [orderNote, setOrderNote] = useState("");
  const [lines, setLines] = useState<Line[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);
  const [orderError, setOrderError] = useState<string | null>(null);
  const [requestOpen, setRequestOpen] = useState(false);
  const [requestSupplier, setRequestSupplier] = useState("");
  const [copied, setCopied] = useState(false);

  const create = useMutation({
    mutationFn: () => {
      const missing = lines.find((line) => !line.productId);
      if (missing) throw new Error("Продукт должен быть из каталога. Заведите его на складе, если позиции нет.");
      return client.createDelivery({
        id: newId(),
        number,
        supplier: supplier.trim() || null,
        items: lines.map((line) => ({
          productId: line.productId,
          name: line.name,
          unit: line.unit,
          quantity: Number(line.quantity),
          expiresAt: `${line.expiresAt}T12:00:00.000Z`,
        })),
      });
    },
    onSuccess: async () => {
      setNumber("");
      setSupplier("");
      setLines([emptyLine()]);
      setError(null);
      await queryClient.invalidateQueries();
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Поставка не принята"),
  });

  const addOrder = useMutation({
    mutationFn: () => client.addPurchaseLine({ productId: orderProduct, quantity: Number(orderQty), note: orderNote.trim() || null }),
    onSuccess: async () => {
      setOrderProduct("");
      setOrderQty("");
      setOrderNote("");
      setOrderError(null);
      await queryClient.invalidateQueries({ queryKey: ["purchase-lines"] });
    },
    onError: (caught) => setOrderError(caught instanceof Error ? caught.message : "Строка не добавилась"),
  });

  function fillFromOrder(rows: PurchaseLine[]) {
    if (rows.length === 0) return;
    setLines(rows.map((row) => ({
      key: newId(),
      productId: row.productId,
      name: row.productName,
      unit: row.unit,
      quantity: String(row.quantity),
      expiresAt: typicalExpiry(row.productName),
    })));
    setRequestOpen(false);
  }

  async function copyRequest(rows: PurchaseLine[]) {
    await navigator.clipboard.writeText(requestText(rows, requestSupplier));
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2000);
  }

  function update(key: string, patch: Partial<Line>) {
    setLines((current) => current.map((line) => line.key === key ? { ...line, ...patch } : line));
  }

  const rows = deliveries.data ?? [];
  const maxBatches = Math.max(1, ...rows.map((delivery) => delivery.batches.length));
  const orderRows = order.data ?? [];

  return (
    <div className={`rep${requestOpen ? " is-supplier-print" : ""}`}>
      <header className="rep-head">
        <div>
          <h1>Поставки</h1>
          <p>Сначала заявка поставщику. Потом приход: факт с накладной и свой «годен до» у каждой пачки.</p>
        </div>
        <Link to="/intake" className="btn-ghost">Приёмка QR</Link>
      </header>
      <Panel title="Заказ поставщику">
        <p className="reason">Внутренний список: кухня и прогноз. Склад ещё не меняется. Поставщику уходит заявка — таблица, копия или CSV. Когда машина приехала — перенесите в приход и снимите срок с пачки.</p>
        {order.isLoading ? <p>Загрузка списка…</p> : null}
        {orderRows.length === 0 && !order.isLoading ? <Empty>Список пуст. Добавьте позицию или дождитесь дописки шефа.</Empty> : null}
        {orderRows.length > 0 ? (
          <table>
            <thead><tr><th>Продукт</th><th>Кол-во</th><th>Кто</th><th>Заметка</th><th></th></tr></thead>
            <tbody>
              {orderRows.map((row) => (
                <tr key={row.id}>
                  <td>{row.productName}</td>
                  <td><Qty value={row.quantity} unit={row.unit} /></td>
                  <td className="reason">{sourceLabel(row.source)} · {row.requestedByLogin}</td>
                  <td className="reason">{row.note ?? "—"}</td>
                  <td>
                    <button type="button" className="btn-ghost" onClick={() => void client.cancelPurchaseLine(row.id).then(() => queryClient.invalidateQueries({ queryKey: ["purchase-lines"] }))}>
                      Убрать
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
        <form className="form" onSubmit={(event) => { event.preventDefault(); addOrder.mutate(); }}>
          <label>
            Продукт
            <select value={orderProduct} onChange={(event) => setOrderProduct(event.target.value)} required>
              <option value="">Из каталога</option>
              {products.data?.map((product) => (
                <option key={product.id} value={product.id}>{product.name}</option>
              ))}
            </select>
          </label>
          <label>Кол-во<input type="number" min="0.001" step="0.001" value={orderQty} onChange={(event) => setOrderQty(event.target.value)} required /></label>
          <label>Заметка<input value={orderNote} onChange={(event) => setOrderNote(event.target.value)} placeholder="банкет, замена…" /></label>
          {orderError ? <p className="error-text">{orderError}</p> : null}
          <div className="btn-row">
            <button className="btn" disabled={addOrder.isPending}>В заказ</button>
            {orderRows.length > 0 ? (
              <>
                <button className="btn-ghost" type="button" onClick={() => setRequestOpen(true)}>Заявка поставщику</button>
                <button className="btn-ghost" type="button" onClick={() => fillFromOrder(orderRows)}>Перенести в приход</button>
              </>
            ) : null}
          </div>
        </form>
      </Panel>
      {requestOpen && orderRows.length > 0 ? (
        <section className="panel supplier-sheet">
          <header className="panel-head">
            <h2>Заявка на поставку</h2>
            <button type="button" className="btn-ghost no-print" onClick={() => setRequestOpen(false)}>Закрыть</button>
          </header>
          <p className="supplier-sheet-meta">{requestDate()}{requestSupplier.trim() ? ` · ${requestSupplier.trim()}` : ""}</p>
          <label className="no-print">
            Кому
            <input value={requestSupplier} onChange={(event) => setRequestSupplier(event.target.value)} placeholder="Северная рыба, Молочный двор…" />
          </label>
          <p className="reason no-print">Это то, что уходит наружу: печать, WhatsApp, письмо. Срока годности здесь нет — его ещё нет, пока пачка не приехала.</p>
          <table className="rep-table">
            <thead><tr><th className="num">№</th><th>Продукт</th><th className="num">Количество</th><th>Комментарий</th></tr></thead>
            <tbody>
              {orderRows.map((row, index) => (
                <tr key={row.id}>
                  <td className="num">{index + 1}</td>
                  <td>{row.productName}</td>
                  <td className="num"><Qty value={row.quantity} unit={row.unit} /></td>
                  <td className="reason">{row.note ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="btn-row no-print">
            <button className="btn" type="button" onClick={() => window.print()}>Печать</button>
            <button className="btn-ghost" type="button" onClick={() => void copyRequest(orderRows)}>{copied ? "Скопировано" : "Копировать"}</button>
            <button className="btn-ghost" type="button" onClick={() => downloadRequestCsv(orderRows)}>Скачать CSV</button>
          </div>
        </section>
      ) : null}
      <Panel title="Приход в холодильник">
          <p className="reason">Годен до — с пачки, у каждой позиции свой. Рыба короче, бакалея дольше. Подставили типичный срок — поправьте по этикетке.</p>
          <form className="form" onSubmit={(event) => { event.preventDefault(); create.mutate(); }}>
            <label className="del-number">Номер накладной<input value={number} onChange={(event) => setNumber(event.target.value)} required /></label>
            <label>Поставщик<input value={supplier} onChange={(event) => setSupplier(event.target.value)} placeholder="Северная рыба" /></label>
            {lines.map((line) => (
              <div className="line" key={line.key}>
                <label>
                  Продукт
                  <select
                    value={line.productId}
                    onChange={(event) => {
                      const product = products.data?.find((item) => item.id === event.target.value);
                      update(line.key, {
                        productId: event.target.value,
                        name: product?.name ?? "",
                        unit: product?.unit ?? line.unit,
                        expiresAt: typicalExpiry(product?.name ?? ""),
                      });
                    }}
                    required
                  >
                    <option value="">Из каталога</option>
                    {products.data?.map((product) => (
                      <option key={product.id} value={product.id}>{product.name}</option>
                    ))}
                  </select>
                </label>
                <label>Кол-во{line.productId ? `, ${unitLabel(line.unit)}` : ""}<input type="number" min="0.001" step="0.001" value={line.quantity} onChange={(event) => update(line.key, { quantity: event.target.value })} required /></label>
                <label>Годен до<input type="date" value={line.expiresAt} onChange={(event) => update(line.key, { expiresAt: event.target.value })} required /></label>
                {lines.length > 1 ? (
                  <button type="button" className="btn-ghost line-remove" onClick={() => setLines((current) => current.filter((item) => item.key !== line.key))} aria-label="Убрать позицию">×</button>
                ) : <span />}
              </div>
            ))}
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
              <thead><tr><th>Номер</th><th>Дата</th><th>Поставщик</th><th>Состав</th><th className="num">Партий</th><th>Ближайший срок</th></tr></thead>
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
                      <td className="reason">{delivery.supplier ?? "—"}</td>
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
