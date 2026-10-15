import { createClient, type ApiClient, type Session } from "@repo/contracts";
import { bindLive, connectLive, LiveEvents } from "@repo/realtime";
import { chatPeer, chatView, noteChatMessage, noteDirect } from "./chat";
import { pushNotice } from "./notices";
import { useQueryClient } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

const storageKey = "monitoring-session";

type Store = {
  session: Session | null;
  client: ApiClient;
};

const SessionContext = createContext<Store | null>(null);
const memory: { session: Session | null } = { session: readSession() };
let notify: (session: Session | null) => void = () => undefined;

function readSession(): Session | null {
  const raw = localStorage.getItem(storageKey);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Session;
  } catch {
    return null;
  }
}

function persist(session: Session | null) {
  memory.session = session;
  if (session) localStorage.setItem(storageKey, JSON.stringify(session));
  else localStorage.removeItem(storageKey);
  notify(session);
}

export const apiBaseUrl = import.meta.env.VITE_API_URL ?? "http://localhost:5080";

const client = createClient({
  baseUrl: apiBaseUrl,
  tokens: {
    getAccess: () => memory.session?.accessToken ?? null,
    getRefresh: () => memory.session?.refreshToken ?? null,
    setSession: persist,
  },
});

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(memory.session);
  const queryClient = useQueryClient();

  useEffect(() => {
    notify = setSession;
    return () => {
      notify = () => undefined;
    };
  }, []);

  useEffect(() => {
    if (!session?.accessToken) return;
    const supplyOnly = session.role === "Supply";
    const connection = connectLive(apiBaseUrl, () => memory.session?.accessToken ?? "");
    const onChat = (payload: { channel?: "Staff" | "System" | "Supply"; text?: string; authorId?: string; recipientId?: string | null }) => {
      void queryClient.invalidateQueries({ queryKey: ["chat"] });
      if (payload.recipientId && payload.authorId) {
        const me = memory.session?.userId;
        const peer = payload.authorId === me ? payload.recipientId : payload.authorId;
        if (!(chatView() === "Direct" && chatPeer() === peer)) noteDirect(peer);
        return;
      }
      const channel = payload.channel ?? "Staff";
      if (channel === "System" && payload.text && !supplyOnly) pushNotice(payload.text);
      if (chatView() !== channel) noteChatMessage(channel);
    };
    if (supplyOnly) {
      connection.on(LiveEvents.chatMessage, onChat);
      return bindLive(connection);
    }
    const refresh = () => {
      for (const key of ["dashboard", "stock", "batches", "stop-list", "priority", "hot-offers", "orders", "deliveries", "purchase-lines", "supply-order", "intake", "write-offs", "deficits", "weekly-forecast", "spend", "kitchen", "reports", "shift-current", "shift-journal", "pending-write-offs", "chat", "staff-pending"]) {
        void queryClient.invalidateQueries({ queryKey: [key] });
      }
    };
    connection.on(LiveEvents.kitchenUpdated, refresh);
    connection.on(LiveEvents.tableProblem, (payload: { tableNumber?: string; dishes?: string; note?: string }) => {
      refresh();
      if (memory.session?.role !== "Waiter") return;
      const table = payload.tableNumber ?? "без номера";
      const dishes = payload.dishes ? ` ${payload.dishes}.` : "";
      const note = payload.note ? ` ${payload.note}.` : "";
      pushNotice(`Стол ${table}: кухня не отдаёт заказ.${dishes}${note} Подойди к гостю.`);
    });
    connection.on(LiveEvents.stockChanged, (payload: { depleted?: string[] }) => {
      refresh();
      for (const name of payload.depleted ?? []) pushNotice(`${name} закончился`);
    });
    connection.on(LiveEvents.batchExpiring, (payload: { batches?: { productName: string }[] }) => {
      refresh();
      for (const batch of payload.batches ?? []) pushNotice(`${batch.productName}: скоро срок`);
    });
    connection.on(LiveEvents.batchExpired, () => {
      refresh();
    });
    connection.on(LiveEvents.stopListUpdated, refresh);
    connection.on(LiveEvents.hotOffersUpdated, refresh);
    connection.on(LiveEvents.shiftUpdated, refresh);
    connection.on(LiveEvents.chatMessage, onChat);
    return bindLive(connection);
  }, [queryClient, session?.accessToken, session?.role]);

  const store = useMemo<Store>(() => ({ session, client }), [session]);
  return <SessionContext.Provider value={store}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const value = useContext(SessionContext);
  if (!value) throw new Error("Сессия не инициализирована");
  return value;
}
