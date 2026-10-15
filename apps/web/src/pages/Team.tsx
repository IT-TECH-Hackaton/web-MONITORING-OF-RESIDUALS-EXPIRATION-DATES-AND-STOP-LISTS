import { ApiError, type Role } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { ChatBox } from "../chat";
import { useSession } from "../session";
import { Empty, ErrorText, Panel } from "../ui";

const roles: Record<string, string> = {
  Manager: "менеджер",
  Waiter: "официант",
  Kitchen: "кухня",
  Supply: "поставка",
};

type InviteDraft = {
  displayName: string;
  login: string;
  password: string;
  role: Role;
  contact: string;
};

const emptyDraft = (): InviteDraft => ({
  displayName: "",
  login: "",
  password: "",
  role: "Waiter",
  contact: "",
});

function makePassword() {
  const alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
  return Array.from({ length: 8 }, () => alphabet[Math.floor(Math.random() * alphabet.length)]).join("");
}

function credentialsText(person: InviteDraft) {
  return [
    "Вход в систему ресторана",
    `Имя: ${person.displayName}`,
    `Роль: ${roles[person.role] ?? person.role}`,
    `Логин: ${person.login}`,
    `Пароль: ${person.password}`,
    `Сайт: ${window.location.origin}`,
  ].join("\n");
}

function phoneDigits(value: string) {
  const digits = value.replace(/\D/g, "");
  if (digits.length === 11 && digits.startsWith("8")) return `+7${digits.slice(1)}`;
  if (digits.length === 11 && digits.startsWith("7")) return `+${digits}`;
  if (digits.length === 10) return `+7${digits}`;
  return digits.length >= 10 ? `+${digits}` : "";
}

function contactKind(value: string): "email" | "phone" | null {
  const trimmed = value.trim();
  if (trimmed.includes("@") && /.+@.+\..+/.test(trimmed)) return "email";
  if (phoneDigits(trimmed)) return "phone";
  return null;
}

export function TeamPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<InviteDraft>(emptyDraft);
  const [issued, setIssued] = useState<InviteDraft | null>(null);
  const [copied, setCopied] = useState(false);
  const people = useQuery({ queryKey: ["staff"], queryFn: () => client.staff() });
  const pending = useQuery({ queryKey: ["staff-pending"], queryFn: () => client.pendingStaff() });
  const invite = useMutation({
    mutationFn: (input: InviteDraft) =>
      client.inviteStaff({
        login: input.login.trim(),
        password: input.password,
        displayName: input.displayName.trim(),
        role: input.role,
      }),
    onSuccess: async (_person, input) => {
      setIssued(input);
      setCopied(false);
      setDraft(emptyDraft());
      await queryClient.invalidateQueries({ queryKey: ["staff"] });
    },
  });
  const decide = useMutation({
    mutationFn: async (input: { id: string; approve: boolean }) => {
      if (input.approve) await client.approveStaff(input.id);
      else await client.rejectStaff(input.id);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["staff-pending"] });
      await queryClient.invalidateQueries({ queryKey: ["staff"] });
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    invite.mutate(draft);
  }

  async function copyIssued() {
    if (!issued) return;
    await navigator.clipboard.writeText(credentialsText(issued));
    setCopied(true);
  }

  function sendMail() {
    if (!issued) return;
    const to = issued.contact.trim();
    const subject = encodeURIComponent("Вход в систему ресторана");
    const body = encodeURIComponent(credentialsText(issued));
    window.location.href = `mailto:${to}?subject=${subject}&body=${body}`;
  }

  function sendSms() {
    if (!issued) return;
    const to = phoneDigits(issued.contact);
    const body = encodeURIComponent(credentialsText(issued));
    const sep = /iPhone|iPad|Mac/.test(navigator.userAgent) ? "&" : "?";
    window.location.href = `sms:${to}${sep}body=${body}`;
  }

  const kind = issued ? contactKind(issued.contact) : null;

  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Команда</h1>
          <p>Менеджер заводит учётки сам и отправляет логин с паролем почтой или SMS. На вход зарегистрироваться нельзя.</p>
        </div>
      </header>
      <Panel title="Новый сотрудник">
        <form className="form" onSubmit={submit}>
          <label>
            Имя
            <input value={draft.displayName} onChange={(event) => setDraft((current) => ({ ...current, displayName: event.target.value }))} required />
          </label>
          <label>
            Роль
            <select value={draft.role} onChange={(event) => setDraft((current) => ({ ...current, role: event.target.value as Role }))}>
              <option value="Waiter">официант</option>
              <option value="Kitchen">кухня</option>
              <option value="Manager">менеджер</option>
              <option value="Supply">поставка</option>
            </select>
          </label>
          <label>
            Логин
            <input value={draft.login} onChange={(event) => setDraft((current) => ({ ...current, login: event.target.value }))} autoComplete="off" required />
          </label>
          <label>
            Пароль
            <input
              type="text"
              value={draft.password}
              onChange={(event) => setDraft((current) => ({ ...current, password: event.target.value }))}
              autoComplete="new-password"
              required
              minLength={6}
            />
          </label>
          <label>
            Почта или телефон
            <input
              value={draft.contact}
              onChange={(event) => setDraft((current) => ({ ...current, contact: event.target.value }))}
              placeholder="anna@restoran.ru или +7 900 123-45-67"
            />
          </label>
          {invite.error ? <p className="error-text">{invite.error instanceof ApiError ? invite.error.message : "Не удалось добавить"}</p> : null}
          <p className="hint">Пароль в базе не читается обратно. Отправьте его сразу, пока он на экране.</p>
          <div className="btn-row">
            <button className="btn" disabled={invite.isPending}>{invite.isPending ? "Добавляем…" : "Добавить и открыть вход"}</button>
            <button type="button" className="btn-ghost" onClick={() => setDraft((current) => ({ ...current, password: makePassword() }))}>Сгенерировать пароль</button>
          </div>
        </form>
      </Panel>
      {issued ? (
        <Panel title="Отправить доступ">
          <p className="reason">
            {issued.displayName} · {roles[issued.role]} · логин {issued.login} · пароль {issued.password}
          </p>
          {issued.contact ? <p className="reason">Куда: {issued.contact}</p> : <p className="hint">Почту или телефон можно дописать ниже и сразу открыть письмо или SMS.</p>}
          <label>
            Почта или телефон
            <input value={issued.contact} onChange={(event) => { setIssued((current) => current ? { ...current, contact: event.target.value } : current); }} />
          </label>
          <div className="btn-row">
            <button className="btn" type="button" onClick={() => void copyIssued()}>{copied ? "Скопировано" : "Копировать"}</button>
            {kind === "email" ? <button className="btn-ghost" type="button" onClick={sendMail}>Письмо</button> : null}
            {kind === "phone" ? <button className="btn-ghost" type="button" onClick={sendSms}>SMS</button> : null}
          </div>
        </Panel>
      ) : null}
      <Panel title="Кто уже в системе">
        {people.isLoading ? <p>Смотрим команду…</p> : null}
        {people.error ? <ErrorText error={people.error} /> : null}
        {people.data?.length === 0 ? <Empty>Пока никого нет.</Empty> : null}
        <div className="stack">
          {people.data?.map((person) => (
            <article key={person.id} className="del-arrived">
              <div>
                <strong>{person.displayName || person.login}</strong>
                <p className="reason">{person.login} · {roles[person.role] ?? person.role}</p>
              </div>
            </article>
          ))}
        </div>
      </Panel>
      {(pending.data?.length ?? 0) > 0 ? (
        <Panel title="Старые заявки">
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
      ) : null}
      <Panel title="Чат">
        <ChatBox />
      </Panel>
    </div>
  );
}
