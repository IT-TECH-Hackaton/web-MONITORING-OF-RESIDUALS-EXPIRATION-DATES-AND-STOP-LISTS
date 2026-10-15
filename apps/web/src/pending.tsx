import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "./session";
import { Empty, ErrorText, Qty } from "./ui";

export function PendingWriteOffs() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const pending = useQuery({ queryKey: ["pending-write-offs"], queryFn: () => client.pendingWriteOffs() });
  const writeOff = useMutation({
    mutationFn: (batch: { id: string; productId: string; quantityRemaining: number }) =>
      client.createWriteOff({
        reason: "Expiry",
        items: [{ productId: batch.productId, quantity: batch.quantityRemaining, batchId: batch.id }],
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["pending-write-offs"] });
    },
  });

  if (pending.isLoading || pending.data?.length === 0) return null;
  return (
    <section className="panel pending-glow">
      <header className="panel-head"><h2>Порча</h2></header>
      <p className="reason">Срок вышел. Эти партии уже не в готовке. Списание убирает их со склада.</p>
      {pending.error ? <ErrorText error={pending.error} /> : null}
      {pending.data?.length === 0 ? <Empty>Просрочки нет.</Empty> : null}
      <div className="stack">
        {pending.data?.map((batch) => (
          <article key={batch.id} className="del-arrived">
            <span className="rep-dot" style={{ background: "var(--accent)" }} />
            <div>
              <strong>{batch.productName}</strong>
              <p className="reason">{batch.deliveryNumber} · <Qty value={batch.quantityRemaining} unit={batch.unit} /></p>
            </div>
            <button
              type="button"
              className="btn"
              disabled={writeOff.isPending}
              onClick={() => writeOff.mutate({ id: batch.id, productId: batch.productId, quantityRemaining: batch.quantityRemaining })}
            >
              Списать
            </button>
          </article>
        ))}
      </div>
    </section>
  );
}
