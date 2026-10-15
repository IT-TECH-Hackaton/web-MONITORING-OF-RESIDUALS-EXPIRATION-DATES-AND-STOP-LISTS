import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, useSyncExternalStore } from "react";
import { Panel } from "./ui";
import { useSession } from "./session";

const roles: Record<string, string> = { Manager: "менеджер", Kitchen: "кухня", Waiter: "официант", System: "система" };

type ChatChannel = "Staff" | "System" | "Direct" | "Supply";

let staffUnread = 0;
let systemUnread = 0;
let directUnread = 0;
let supplyUnread = 0;
let directVersion = 0;
const directPeers = new Map<string, number>();
let viewing: ChatChannel | null = null;
let viewingPeer: string | null = null;
const unreadListeners = new Set<() => void>();

function emitUnread() {
  unreadListeners.forEach((listener) => listener());
}

export function noteChatMessage(channel: ChatChannel = "Staff") {
  if (channel === "System") systemUnread += 1;
  else if (channel === "Direct") directUnread += 1;
  else if (channel === "Supply") supplyUnread += 1;
  else staffUnread += 1;
  emitUnread();
}

export function noteDirect(peerId: string) {
  directUnread += 1;
  directPeers.set(peerId, (directPeers.get(peerId) ?? 0) + 1);
  directVersion += 1;
  emitUnread();
}

export function markChatRead(channel: ChatChannel = "Staff") {
  if (channel === "System") systemUnread = 0;
  else if (channel === "Direct") directUnread = 0;
  else if (channel === "Supply") supplyUnread = 0;
  else staffUnread = 0;
  emitUnread();
}

export function markDirectRead(peerId: string) {
  const count = directPeers.get(peerId) ?? 0;
  if (count === 0) return;
  directPeers.delete(peerId);
  directUnread = Math.max(0, directUnread - count);
  directVersion += 1;
  emitUnread();
}

export function chatView() {
  return viewing;
}

export function chatPeer() {
  return viewingPeer;
}

export function useDirectVersion() {
  return useSyncExternalStore(
    (listener) => {
      unreadListeners.add(listener);
      return () => unreadListeners.delete(listener);
    },
    () => directVersion,
  );
}

export function peerUnread(peerId: string) {
  return directPeers.get(peerId) ?? 0;
}

export function useChatUnread() {
  return useSyncExternalStore(
    (listener) => {
      unreadListeners.add(listener);
      return () => unreadListeners.delete(listener);
    },
    () => staffUnread + systemUnread + directUnread + supplyUnread,
  );
}

export function useChannelUnread(channel: ChatChannel) {
  return useSyncExternalStore(
    (listener) => {
      unreadListeners.add(listener);
      return () => unreadListeners.delete(listener);
    },
    () => {
      if (channel === "System") return systemUnread;
      if (channel === "Direct") return directUnread;
      if (channel === "Supply") return supplyUnread;
      return staffUnread;
    },
  );
}

export function KitchenChatPage() {
  return (
    <div className="rep">
      <header className="rep-head">
        <div>
          <h1>Чат</h1>
          <p>Смена — всем. Личные — одному человеку. Система — порча и срок, туда писать нельзя.</p>
        </div>
      </header>
      <Panel title="Чат смены">
        <ChatBox />
      </Panel>
    </div>
  );
}

export function ChatBox({ only }: { only?: "supply" }) {
  const { client, session } = useSession();
  const queryClient = useQueryClient();
  const messages = useQuery({ queryKey: ["chat"], queryFn: () => client.chat() });
  const people = useQuery({ queryKey: ["chat-people"], queryFn: () => client.chatPeople(), enabled: only !== "supply" });
  const [channel, setChannel] = useState<ChatChannel>(only === "supply" ? "Supply" : "Staff");
  const supplyBadge = useChannelUnread("Supply");
  const canSupplier = only === "supply" || session?.role === "Manager" || session?.role === "Kitchen";
  const [peerId, setPeerId] = useState("");
  const systemUnread = useChannelUnread("System");
  const staffUnread = useChannelUnread("Staff");
  const directBadge = useChannelUnread("Direct");
  useDirectVersion();
  useEffect(() => {
    viewing = channel;
    viewingPeer = channel === "Direct" ? peerId || null : null;
    if (channel === "Direct") {
      if (peerId) markDirectRead(peerId);
    } else {
      markChatRead(channel);
    }
    return () => {
      viewing = null;
      viewingPeer = null;
    };
  }, [channel, peerId, messages.data]);
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const send = useMutation({
    mutationFn: () => client.postChat(text.trim(), channel === "Direct" ? peerId : null, channel === "Supply"),
    onSuccess: async () => {
      setText("");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["chat"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Не отправилось"),
  });
  const mine = session?.userId;
  const rows = (messages.data ?? []).filter((message) => {
    if (channel === "Direct") {
      if (!peerId || !message.recipientId) return false;
      return (message.authorId === mine && message.recipientId === peerId) || (message.authorId === peerId && message.recipientId === mine);
    }
    return (message.channel ?? "Staff") === channel && !message.recipientId;
  });
  const peer = people.data?.find((person) => person.id === peerId);

  return (
    <section className="chat-box">
      {only === "supply" ? null : <div className="chat-tabs">
        <button type="button" className={channel === "Staff" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setChannel("Staff")}>
          Смена
          {staffUnread > 0 && channel !== "Staff" ? <span className="chat-unread">{staffUnread > 9 ? "9+" : staffUnread}</span> : null}
        </button>
        <button type="button" className={channel === "Direct" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setChannel("Direct")}>
          Личные
          {directBadge > 0 && channel !== "Direct" ? <span className="chat-unread">{directBadge > 9 ? "9+" : directBadge}</span> : null}
        </button>
        <button type="button" className={channel === "System" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setChannel("System")}>
          Система
          {systemUnread > 0 && channel !== "System" ? <span className="chat-unread">{systemUnread > 9 ? "9+" : systemUnread}</span> : null}
        </button>
        {canSupplier ? (
          <button type="button" className={channel === "Supply" ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setChannel("Supply")}>
            Поставщик
            {supplyBadge > 0 && channel !== "Supply" ? <span className="chat-unread">{supplyBadge > 9 ? "9+" : supplyBadge}</span> : null}
          </button>
        ) : null}
      </div>}
      {channel === "Direct" ? (
        <div className="chat-people">
          {(people.data ?? []).map((person) => (
            <button key={person.id} type="button" className={peerId === person.id ? "btn-ghost is-on" : "btn-ghost"} onClick={() => setPeerId(person.id)}>
              {person.displayName || person.login}
              <span>{roles[person.role] ?? person.role}</span>
              {peerUnread(person.id) > 0 ? <span className="chat-unread">{peerUnread(person.id) > 9 ? "9+" : peerUnread(person.id)}</span> : null}
            </button>
          ))}
        </div>
      ) : null}
      <div className="chat-log">
        {channel === "Direct" && !peerId ? <p className="reason">Выберите, кому написать.</p> : null}
        {rows.length === 0 && (channel !== "Direct" || peerId) ? <p className="reason">{channel === "System" ? "Порчи пока не было." : channel === "Direct" ? `С ${peer?.displayName ?? "этим человеком"} пока тихо.` : "Пока тихо."}</p> : null}
        {rows.map((message) => (
          <article key={message.id} className={message.channel === "System" ? "is-system" : message.authorId === mine ? "is-mine" : undefined}>
            <header>
              <strong>{message.author}</strong>
              <span>{roles[message.role] ?? message.role}</span>
            </header>
            <p>{message.text}</p>
          </article>
        ))}
      </div>
      {channel === "System" ? <p className="reason">Это лента склада. Сообщения пишет система, когда партия переходит в порчу.</p> : (
        <form className="chat-form" onSubmit={(event) => { event.preventDefault(); if (text.trim()) send.mutate(); }}>
          <input value={text} onChange={(event) => setText(event.target.value)} placeholder={channel === "Supply" ? (session?.role === "Supply" ? "Написать заведению" : "Написать поставщику") : channel === "Direct" ? "Личное сообщение" : "Сообщение смене"} maxLength={500} />
          <button className="btn" type="submit" disabled={send.isPending || text.trim().length === 0 || (channel === "Direct" && !peerId)}>Отправить</button>
        </form>
      )}
      {error ? <p className="error-text">{error}</p> : null}
    </section>
  );
}
