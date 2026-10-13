import { formatHours, formatQty } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "../session";
import { DishTile, Empty, ErrorText, Panel } from "../ui";

export function KitchenPage() {
  const { client } = useSession();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });
  const board = useQuery({ queryKey: ["kitchen"], queryFn: () => client.kitchen() });

  if (stop.isLoading) return <p>Собираем смену…</p>;
  if (stop.error) return <ErrorText error={stop.error} />;

  const hotIds = new Set(offers.data?.map((offer) => offer.dishId));
  const dishes = [...(stop.data ?? [])].sort((a, b) => Number(b.isStopped) - Number(a.isStopped) || a.portionsPossible - b.portionsPossible);

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Смена</h1>
          <p>Плитки как на кассе: стоп, мало, приоритет. Плюс порций после доготовки подключим следующим шагом.</p>
        </div>
      </div>
      {dishes.length === 0 ? <Empty>Блюд в меню нет.</Empty> : (
        <div className="tiles">
          {dishes.map((entry) => (
            <DishTile key={entry.dishId} entry={entry} priority={hotIds.has(entry.dishId) && !entry.isStopped} />
          ))}
        </div>
      )}
      <Panel title="Использовать первым">
        {board.isLoading ? <p>Партии…</p> : null}
        {board.error ? <ErrorText error={board.error} /> : null}
        {board.data?.batches.length === 0 ? <Empty>Нет коротких и вскрытых партий.</Empty> : (
          <div className="stack">
            {board.data?.batches.map((batch) => (
              <article key={batch.batchId}>
                <strong>{batch.productName}</strong>
                <p className="reason">
                  {batch.deliveryNumber} · {formatQty(batch.remaining, batch.unit)} · {formatHours(batch.hoursLeft)}
                  {batch.openedAt ? " · вскрыта" : ""}
                </p>
              </article>
            ))}
          </div>
        )}
      </Panel>
    </>
  );
}
