import { createClient, type ApiClient, type Session } from "@repo/contracts";
import { connectLive, LiveEvents } from "@repo/realtime";
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
    if (!session?.accessToken || (session.role !== "Manager" && session.role !== "Kitchen")) return;
    const connection = connectLive(apiBaseUrl, () => memory.session?.accessToken ?? "");
    const refresh = () => {
      for (const key of ["dashboard", "stock", "batches", "stop-list", "hot-offers", "orders", "deliveries", "write-offs", "deficits", "weekly-forecast", "kitchen"]) {
        void queryClient.invalidateQueries({ queryKey: [key] });
      }
    };
    connection.on(LiveEvents.kitchenUpdated, refresh);
    connection.on(LiveEvents.stockChanged, refresh);
    connection.on(LiveEvents.batchExpiring, refresh);
    connection.on(LiveEvents.batchExpired, refresh);
    connection.on(LiveEvents.stopListUpdated, refresh);
    connection.on(LiveEvents.hotOffersUpdated, refresh);
    void connection.start().catch(() => undefined);
    return () => {
      void connection.stop();
    };
  }, [queryClient, session?.accessToken, session?.role]);

  const store = useMemo<Store>(() => ({ session, client }), [session]);
  return <SessionContext.Provider value={store}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const value = useContext(SessionContext);
  if (!value) throw new Error("Сессия не инициализирована");
  return value;
}
