import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChatBox } from "../chat";
import { useSession } from "../session";
import { Empty, ErrorText, Panel } from "../ui";

const roles: Record<string, string> = { Kitchen: "кухня", Waiter: "официант" };

export function TeamPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const pending = useQuery({ queryKey: ["staff-pending"], queryFn: () => client.pendingStaff() });
  const decide = useMutation({
    mutationFn: async (input: { id: string; approve: boolean }) => {
      if (input.approve) await client.approveStaff(input.id);
      else await client.rejectStaff(input.id);
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["staff-pending"] }); },
  });

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Команда</h1>
          <p>Заявки на вход подтверждает менеджер. Чат общий для зала, кухни и менеджера.</p>
        </div>
      </header>
      <Panel title="Заявки">
        {pending.isLoading ? <p>Смотрим заявки…</p> : null}
        {pending.error ? <ErrorText error={pending.error} /> : null}
        {pending.data?.length === 0 ? <Empty>Новых заявок нет.</Empty> : null}
        <div className="stack">
          {pending.data?.map((person) => (
            <article key={person.id} className="del-arrived">
              <div>
                <strong>{person.displayName}</strong>
                <p className="reason">{person.login} · {roles[person.role] ?? person.role}</p>
              </div>
              <button type="button" className="btn" disabled={decide.isPending} onClick={() => decide.mutate({ id: person.id, approve: true })}>Подтвердить</button>
              <button type="button" className="btn-ghost" disabled={decide.isPending} onClick={() => decide.mutate({ id: person.id, approve: false })}>Отклонить</button>
            </article>
          ))}
        </div>
      </Panel>
      <Panel title="Чат">
        <ChatBox />
      </Panel>
    </div>
  );
}
