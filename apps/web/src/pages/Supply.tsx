import { unitLabel, type PurchaseSource, type Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { ChatBox } from "../chat";
import { useSession } from "../session";
import { Empty, ErrorText, Panel, Qty } from "../ui";

function sourceLabel(source: PurchaseSource) {
  if (source === "Chef") return "кухня";
  if (source === "Forecast") return "прогноз";
  return "менеджер";
}

function plusDays(days: number) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
}

type ExtraLine = { key: string; productId: string; quantity: string; expiresAt: string };
type OfferLine = { productId: string; name: string; unit: Unit; quantity: string; expiresAt: string };
type SupplyTab = "standard" | "order" | "extra" | "chat";

const emptyExtra = (): ExtraLine => ({ key: crypto.randomUUID(), productId: "", quantity: "", expiresAt: plusDays(7) });

export function SupplyPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<SupplyTab>("standard");
  const order = useQuery({ queryKey: ["supply-order"], queryFn: () => client.supplyOrder(), refetchInterval: 5000 });
  const standard = useQuery({ queryKey: ["supply-standard"], queryFn: () => client.supplyStandard() });
  const catalog = useQuery({ queryKey: ["supply-catalog"], queryFn: () => client.supplyCatalog(), enabled: tab === "extra" });
  const [offer, setOffer] = useState<OfferLine[] | null>(null);
  const [proposalSent, setProposalSent] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);
  const [extraSent, setExtraSent] = useState<string | null>(null);
  const [extraError, setExtraError] = useState<string | null>(null);
  const [lines, setLines] = useState<ExtraLine[]>([emptyExtra()]);
  useEffect(() => {
    if (!standard.data || offer) return;
    setOffer(standard.data.map((line) => ({
      productId: line.id,
      name: line.name,
      unit: line.unit,
      quantity: String(line.quantity),
      expiresAt: line.expiresAt.slice(0, 10),
    })));
  }, [standard.data, offer]);
  const proposal = useMutation({
    mutationFn: () => client.sendSupplyProposal(
      (offer ?? [])
        .filter((line) => Number(line.quantity) > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          expiresAt: `${line.expiresAt}T12:00:00.000Z`,
        })),
    ),
    onSuccess: async (delivery) => {
      setProposalSent(`Предложение ${delivery.number} ушло менеджеру. На складе его ещё нет.`);
    },
  });
  const send = useMutation({
    mutationFn: () => client.sendSupply(),
    onSuccess: async (delivery) => {
      setSent(`Предложение ${delivery.number} ушло. На складе его ещё нет.`);
      await queryClient.invalidateQueries({ queryKey: ["supply-order"] });
    },
  });
  const extra = useMutation({
    mutationFn: () => client.sendSupplyExtra(lines.map((line) => ({
      productId: line.productId,
      quantity: Number(line.quantity),
      expiresAt: `${line.expiresAt}T12:00:00.000Z`,
    }))),
    onSuccess: async (delivery) => {
      setExtraError(null);
      setExtraSent(`${delivery.number} ушло как «приехала». Это сверх заказа, склад ещё не вырос.`);
      setLines([emptyExtra()]);
    },
    onError: (caught) => setExtraError(caught instanceof Error ? caught.message : "Не отправилось"),
  });
  const rows = order.data ?? [];

  function patch(key: string, next: Partial<ExtraLine>) {
    setLines((current) => current.map((line) => line.key === key ? { ...line, ...next } : line));
  }

  function submitExtra() {
    for (const line of lines) {
      const quantity = Number(line.quantity);
      if (!line.productId) {
        setExtraError("Выберите продукт из каталога.");
        return;
      }
      if (!Number.isFinite(quantity) || quantity <= 0 || quantity > 1_000_000_000) {
        setExtraError("Количество от нуля до миллиарда.");
        return;
      }
      if (!line.expiresAt) {
        setExtraError("Укажите срок с пачки.");
        return;
      }
    }
    setExtraError(null);
    extra.mutate();
  }

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Поставка</h1>
          <p>Стандартный список уходит менеджеру. Он правит количества и отдаёт в приёмку. Мелочёвка — отдельно. Склад от этого не растёт.</p>
        </div>
      </header>
      <div className="chat-tabs">
        <button type="button" className={tab === "standard" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setTab("standard")}>Стандарт</button>
        <button type="button" className={tab === "order" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setTab("order")}>Заказ</button>
        <button type="button" className={tab === "extra" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setTab("extra")}>Мелочёвка</button>
        <button type="button" className={tab === "chat" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setTab("chat")}>Чат</button>
      </div>
      {tab === "standard" ? (
        <Panel title="Стандартная закупка">
          <p className="reason">Обычный набор заведения. «Годен до» — пример от сегодняшнего дня. Ноль — не предлагать. На приёмке кухня поправит дату по пачке.</p>
          {standard.isLoading ? <p>Собираем список…</p> : null}
          {standard.error ? <ErrorText error={standard.error} /> : null}
          {offer && offer.length > 0 ? (
            <div className="offer-scroll">
              <table>
                <thead><tr><th>Продукт</th><th>Количество</th><th>Годен до</th></tr></thead>
                <tbody>
                  {offer.map((line) => (
                    <tr key={line.productId}>
                      <td>{line.name}</td>
                      <td>
                        <input
                          type="number"
                          min="0"
                          step="1"
                          value={line.quantity}
                          aria-label={`Количество: ${line.name}`}
                          onChange={(event) => setOffer((current) => current?.map((item) => item.productId === line.productId ? { ...item, quantity: event.target.value } : item) ?? current)}
                        />
                        <span className="reason"> {unitLabel(line.unit)}</span>
                      </td>
                      <td>
                        <input
                          type="date"
                          value={line.expiresAt}
                          aria-label={`Годен до: ${line.name}`}
                          onChange={(event) => setOffer((current) => current?.map((item) => item.productId === line.productId ? { ...item, expiresAt: event.target.value } : item) ?? current)}
                          required
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
          {proposalSent ? <p className="hint">{proposalSent}</p> : null}
          {proposal.error ? <ErrorText error={proposal.error} /> : null}
          <div className="btn-row">
            <button type="button" className="btn" disabled={proposal.isPending || !offer?.some((line) => Number(line.quantity) > 0 && line.expiresAt)} onClick={() => proposal.mutate()}>
              {proposal.isPending ? "Отправляем…" : "Отправить менеджеру"}
            </button>
          </div>
        </Panel>
      ) : null}
      {tab === "order" ? (
        <Panel title="Заказ заведения">
          <p className="reason">Открытые строки. Сформировать и отправить — машина «приехала», склад не растёт.</p>
          {order.isLoading ? <p>Загрузка списка…</p> : null}
          {order.error ? <ErrorText error={order.error} /> : null}
          {rows.length === 0 && !order.isLoading ? <Empty>Открытого заказа нет.</Empty> : null}
          {rows.length > 0 ? (
            <table>
              <thead><tr><th>Продукт</th><th>Кол-во</th><th>Кто</th><th>Заметка</th></tr></thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>{row.productName}</td>
                    <td><Qty value={row.quantity} unit={row.unit} /></td>
                    <td className="reason">{sourceLabel(row.source)} · {row.requestedByLogin}</td>
                    <td className="reason">{row.note ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
          {sent ? <p className="hint">{sent}</p> : null}
          {send.error ? <ErrorText error={send.error} /> : null}
          <div className="btn-row">
            <button type="button" className="btn" disabled={send.isPending || rows.length === 0} onClick={() => send.mutate()}>
              Сформировать и отправить
            </button>
          </div>
        </Panel>
      ) : null}
      {tab === "extra" ? (
        <Panel title="Мелочёвка">
          <p className="reason">То, чего нет в стандарте. Срок — с пачки. Уходит отдельной машиной и стандартный список не закрывает.</p>
          {catalog.isLoading ? <p>Загрузка каталога…</p> : null}
          {catalog.error ? <ErrorText error={catalog.error} /> : null}
          <div className="stack">
            {lines.map((line) => {
              const product = catalog.data?.find((item) => item.id === line.productId);
              return (
                <div className="line" key={line.key}>
                  <label>
                    Продукт
                    <select
                      value={line.productId}
                      onChange={(event) => patch(line.key, { productId: event.target.value })}
                      required
                    >
                      <option value="">Из каталога</option>
                      {catalog.data?.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
                    </select>
                  </label>
                  <label>
                    Кол-во{product ? `, ${unitLabel(product.unit as Unit)}` : ""}
                    <input type="number" min="0.001" step="0.001" value={line.quantity} onChange={(event) => patch(line.key, { quantity: event.target.value })} required />
                  </label>
                  <label>
                    Годен до
                    <input type="date" value={line.expiresAt} onChange={(event) => patch(line.key, { expiresAt: event.target.value })} required />
                  </label>
                  {lines.length > 1 ? (
                    <button type="button" className="btn-ghost line-remove" onClick={() => setLines((current) => current.filter((item) => item.key !== line.key))} aria-label="Убрать позицию">×</button>
                  ) : <span />}
                </div>
              );
            })}
          </div>
          {extraSent ? <p className="hint">{extraSent}</p> : null}
          {extraError ? <p className="error-text">{extraError}</p> : null}
          <div className="btn-row">
            <button type="button" className="btn" disabled={extra.isPending} onClick={submitExtra}>Привезти</button>
            <button type="button" className="btn-ghost" onClick={() => setLines((current) => [...current, emptyExtra()])}>Ещё позиция</button>
          </div>
        </Panel>
      ) : null}
      {tab === "chat" ? (
        <Panel title="Чат с заведением">
          <p className="reason">Это не общая смена. Пишете менеджеру и кухне, они отвечают здесь же.</p>
          <ChatBox only="supply" />
        </Panel>
      ) : null}
    </div>
  );
}
