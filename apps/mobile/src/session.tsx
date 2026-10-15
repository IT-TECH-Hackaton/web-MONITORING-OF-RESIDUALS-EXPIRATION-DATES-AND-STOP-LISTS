import AsyncStorage from "@react-native-async-storage/async-storage";
import { createClient, type ApiClient, type Session } from "@repo/contracts";
import { bindLive, connectLive, LiveEvents } from "@repo/realtime";
import { useQueryClient } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { Alert } from "react-native";

const sessionKey = "monitoring-session";
const urlKey = "monitoring-api-url";

type Store = {
  ready: boolean;
  session: Session | null;
  baseUrl: string;
  client: ApiClient;
  setBaseUrl: (url: string) => void;
};

const SessionContext = createContext<Store | null>(null);
const memory: { session: Session | null; baseUrl: string } = {
  session: null,
  baseUrl: process.env.EXPO_PUBLIC_API_URL ?? "http://localhost:5080",
};
let notify: (session: Session | null) => void = () => undefined;

function persist(session: Session | null) {
  memory.session = session;
  if (session) void AsyncStorage.setItem(sessionKey, JSON.stringify(session));
  else void AsyncStorage.removeItem(sessionKey);
  notify(session);
}

const client = createClient({
  baseUrl: () => memory.baseUrl,
  tokens: {
    getAccess: () => memory.session?.accessToken ?? null,
    getRefresh: () => memory.session?.refreshToken ?? null,
    setSession: persist,
  },
});

export function SessionProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [session, setSession] = useState<Session | null>(null);
  const [baseUrl, setBaseUrlState] = useState(memory.baseUrl);
  const queryClient = useQueryClient();

  useEffect(() => {
    notify = setSession;
    return () => {
      notify = () => undefined;
    };
  }, []);

  useEffect(() => {
    void (async () => {
      const [storedSession, storedUrl] = await Promise.all([
        AsyncStorage.getItem(sessionKey),
        AsyncStorage.getItem(urlKey),
      ]);
      if (storedUrl) memory.baseUrl = storedUrl;
      if (storedSession) {
        try {
          memory.session = JSON.parse(storedSession) as Session;
        } catch {
          memory.session = null;
        }
      }
      setBaseUrlState(memory.baseUrl);
      setSession(memory.session);
      setReady(true);
    })();
  }, []);

  useEffect(() => {
    if (!session?.accessToken || session.role !== "Waiter") return;
    const connection = connectLive(memory.baseUrl, () => memory.session?.accessToken ?? "");
    const refresh = () => {
      void queryClient.invalidateQueries({ queryKey: ["stop-list"] });
      void queryClient.invalidateQueries({ queryKey: ["priority"] });
      void queryClient.invalidateQueries({ queryKey: ["hot-offers"] });
    };
    connection.on(LiveEvents.stopListUpdated, refresh);
    connection.on(LiveEvents.shiftUpdated, refresh);
    connection.on(LiveEvents.hotOffersUpdated, refresh);
    connection.on(LiveEvents.stockChanged, refresh);
    connection.on(LiveEvents.batchExpired, refresh);
    connection.on(LiveEvents.kitchenUpdated, () => {
      void queryClient.invalidateQueries({ queryKey: ["orders"] });
    });
    connection.on(LiveEvents.tableProblem, (payload: { tableNumber?: string; dishes?: string; note?: string }) => {
      void queryClient.invalidateQueries({ queryKey: ["orders"] });
      const table = payload.tableNumber ?? "без номера";
      const dishes = payload.dishes ? `\n${payload.dishes}` : "";
      const note = payload.note ? `\n${payload.note}` : "";
      Alert.alert(`Стол ${table}`, `Кухня не отдаёт заказ.${dishes}${note}\nПодойди к гостю.`);
    });
    return bindLive(connection);
  }, [queryClient, session?.accessToken, session?.role, baseUrl]);

  const store = useMemo<Store>(() => ({
    ready,
    session,
    baseUrl,
    client,
    setBaseUrl: (url) => {
      memory.baseUrl = url.replace(/\/$/, "");
      setBaseUrlState(memory.baseUrl);
      void AsyncStorage.setItem(urlKey, memory.baseUrl);
    },
  }), [baseUrl, ready, session]);

  return <SessionContext.Provider value={store}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const value = useContext(SessionContext);
  if (!value) throw new Error("Сессия не инициализирована");
  return value;
}
