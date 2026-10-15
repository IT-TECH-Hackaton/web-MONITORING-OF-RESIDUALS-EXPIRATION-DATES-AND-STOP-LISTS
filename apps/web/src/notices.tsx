import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useSession } from "./session";

type Notice = { id: string; text: string; batchId?: string; productId?: string; quantity?: number };

let push: (notice: Notice) => void = () => undefined;

export function pushNotice(text: string, extra?: { batchId: string; productId: string; quantity: number }) {
  push({ id: crypto.randomUUID(), text, ...extra });
}

export function LiveNotices() {
  const { client, session } = useSession();
  const queryClient = useQueryClient();
  const [items, setItems] = useState<Notice[]>([]);
  const canWrite = session?.role === "Manager" || session?.role === "Kitchen";
  const writeOff = useMutation({
    mutationFn: (notice: Notice) => client.createWriteOff({
      reason: "Expiry",
      items: [{ productId: notice.productId!, quantity: notice.quantity!, batchId: notice.batchId }],
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["pending-write-offs"] });
    },
  });

  useEffect(() => {
    push = (notice) => setItems((current) => [notice, ...current].slice(0, 4));
    return () => {
      push = () => undefined;
    };
  }, []);

  if (items.length === 0) return null;
  return (
    <div className="live-notices">
      {items.map((notice) => (
        <article key={notice.id}>
          <p>{notice.text}</p>
          <div className="btn-row">
            {canWrite && notice.batchId ? (
              <button type="button" className="btn" disabled={writeOff.isPending} onClick={() => writeOff.mutate(notice)}>Списать</button>
            ) : null}
            <button type="button" className="btn-ghost" onClick={() => setItems((current) => current.filter((item) => item.id !== notice.id))}>Скрыть</button>
          </div>
        </article>
      ))}
    </div>
  );
}
