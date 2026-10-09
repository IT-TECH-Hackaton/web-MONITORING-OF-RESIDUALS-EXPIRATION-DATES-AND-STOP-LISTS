import { useQuery } from "@tanstack/react-query";
import { useSession } from "../session";
import { Empty, ErrorText, Panel } from "../ui";

export function OffersPage() {
  const { client } = useSession();
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  return (
    <>
      <div className="page-title">
        <div>
          <h1>Горячие блюда</h1>
          <p>Чем ближе срок партии, которую спишут следующей, тем выше блюдо в списке.</p>
        </div>
      </div>
      <div className="split">
        <Panel title="Рекомендации">
          {offers.isLoading ? <p>Считаем приоритет…</p> : null}
          {offers.error ? <ErrorText error={offers.error} /> : null}
          {offers.data?.length === 0 ? <Empty>Нет блюд, которыми можно спасти истекающие партии.</Empty> : null}
          <div className="stack">
            {offers.data?.map((offer, index) => (
              <article key={offer.dishId}>
                <strong>{index + 1}. {offer.dishName}</strong>
                <p className="reason">{offer.reason}</p>
              </article>
            ))}
          </div>
        </Panel>
        <Panel title="Стоп-лист">
          {stop.data?.filter((entry) => entry.isStopped).length === 0 ? <Empty>Все блюда можно приготовить.</Empty> : null}
          <div className="stack">
            {stop.data?.filter((entry) => entry.isStopped).map((entry) => (
              <article key={entry.dishId}>
                <strong>{entry.dishName}</strong>
                <p className="reason">{entry.reason}</p>
              </article>
            ))}
          </div>
        </Panel>
      </div>
    </>
  );
}
