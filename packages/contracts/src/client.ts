import { ApiError, type Dashboard, type Deficit, type Delivery, type DeliveryLineInput, type Dish, type HotOffer, type KitchenBoard, type KitchenStatus, type Order, type Product, type PurchaseDraft, type PurchaseLine, type QrIntakeResult, type Session, type ShiftCurrent, type ShiftHistoryEntry, type ShiftJournalRow, type StockItem, type StopEntry, type Unit, type MoneyReport, type SpendReport, type WeeklyForecastReport, type WriteOff, type WriteOffReason, type Batch } from "./types";

export type TokenStore = {
  getAccess: () => string | null;
  getRefresh: () => string | null;
  setSession: (session: Session | null) => void;
};

export type ApiClient = ReturnType<typeof createClient>;

export function createClient(options: { baseUrl: string | (() => string); tokens: TokenStore }) {
  const resolve = () => (typeof options.baseUrl === "function" ? options.baseUrl() : options.baseUrl).replace(/\/$/, "");
  let refreshing: Promise<boolean> | null = null;

  async function request(path: string, init: RequestInit = {}, retry = true): Promise<Response> {
    const headers = new Headers(init.headers);
    if (init.body && !headers.has("Content-Type")) {
      headers.set("Content-Type", "application/json");
    }
    const access = options.tokens.getAccess();
    if (access) {
      headers.set("Authorization", `Bearer ${access}`);
    }

    const response = await fetch(`${resolve()}${path}`, { ...init, headers });
    if (response.status !== 401 || !retry || path.startsWith("/api/auth/")) {
      return response;
    }

    refreshing ??= refresh().finally(() => {
      refreshing = null;
    });
    const ok = await refreshing;
    return ok ? request(path, init, false) : response;
  }

  async function refresh(): Promise<boolean> {
    const refreshToken = options.tokens.getRefresh();
    if (!refreshToken) return false;
    const response = await fetch(`${resolve()}/api/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    if (!response.ok) {
      options.tokens.setSession(null);
      return false;
    }
    options.tokens.setSession((await response.json()) as Session);
    return true;
  }

  async function json<T>(response: Response): Promise<T> {
    const text = await response.text();
    const body = text ? JSON.parse(text) : null;
    if (!response.ok) {
      throw new ApiError(body?.message ?? body?.rejectionReason ?? "Запрос не выполнен", response.status, body?.items ? body : undefined);
    }
    return body as T;
  }

  return {
    get baseUrl() {
      return resolve();
    },
    async login(login: string, password: string) {
      const response = await request("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ login, password }),
      });
      const session = await json<Session>(response);
      options.tokens.setSession(session);
      return session;
    },
    logout() {
      options.tokens.setSession(null);
    },
    products: () => request("/api/products").then((response) => json<Product[]>(response)),
    createProduct: (name: string, unit: Unit) =>
      request("/api/products", { method: "POST", body: JSON.stringify({ name, unit }) }).then((response) => json<Product>(response)),
    dishes: () => request("/api/dishes").then((response) => json<Dish[]>(response)),
    saveDish: (input: { id?: string; name: string; ingredients: { productId: string; quantity: number }[] }) => {
      const path = input.id ? `/api/dishes/${input.id}` : "/api/dishes";
      return request(path, {
        method: input.id ? "PUT" : "POST",
        body: JSON.stringify({ name: input.name, ingredients: input.ingredients }),
      }).then((response) => json<Dish>(response));
    },
    deliveries: () => request("/api/deliveries").then((response) => json<Delivery[]>(response)),
    delivery: (id: string) => request(`/api/deliveries/${id}`).then((response) => json<Delivery>(response)),
    createDelivery: (input: { id: string; number: string; createdAt?: string | null; supplier?: string | null; items: DeliveryLineInput[] }) =>
      request("/api/deliveries", { method: "POST", body: JSON.stringify(input) }).then((response) => json<Delivery>(response)),
    purchaseLines: (open = true) =>
      request(`/api/purchase-lines?open=${open}`).then((response) => json<PurchaseLine[]>(response)),
    addPurchaseLine: (input: { productId: string; quantity: number; note?: string | null }) =>
      request("/api/purchase-lines", { method: "POST", body: JSON.stringify(input) }).then((response) => json<PurchaseLine>(response)),
    cancelPurchaseLine: (id: string) =>
      request(`/api/purchase-lines/${id}`, { method: "DELETE" }).then(async (response) => {
        if (!response.ok) {
          await json(response);
        }
      }),
    addForecastToOrder: (productIds?: string[]) =>
      request("/api/analytics/purchase-lines", {
        method: "POST",
        body: JSON.stringify({ productIds: productIds ?? null }),
      }).then((response) => json<{ count: number }>(response)),
    qrSample: (valid: boolean) =>
      request(`/api/deliveries/qr-sample?valid=${valid}`).then((response) => json<{ payload: string }>(response)),
    scanDelivery: (payload: string) =>
      request("/api/deliveries/scan", { method: "POST", body: JSON.stringify({ payload }) }).then((response) => json<QrIntakeResult>(response)),
    stock: () => request("/api/stock").then((response) => json<StockItem[]>(response)),
    batches: (productId: string) => request(`/api/stock/${productId}/batches`).then((response) => json<Batch[]>(response)),
    stopList: () => request("/api/stop-list").then((response) => json<StopEntry[]>(response)),
    priority: () => request("/api/priority").then((response) => json<StopEntry[]>(response)),
    hotOffers: () => request("/api/hot-offers").then((response) => json<HotOffer[]>(response)),
    dashboard: () => request("/api/dashboard").then((response) => json<Dashboard>(response)),
    orders: () => request("/api/orders").then((response) => json<Order[]>(response)),
    async placeOrder(input: { id: string; connectionId?: string | null; tableNumber?: string | null; items: { dishId: string; portions: number }[] }) {
      const response = await request("/api/orders", { method: "POST", body: JSON.stringify(input) });
      const text = await response.text();
      const body = text ? (JSON.parse(text) as Order & { message?: string }) : null;
      if (response.status === 409 && body) {
        throw new ApiError(body.rejectionReason ?? "Недостаточно продуктов", 409, body);
      }
      if (!response.ok) {
        throw new ApiError(body?.message ?? "Не удалось создать заказ", response.status);
      }
      return body as Order;
    },
    writeOffs: (reason?: string) =>
      request(`/api/analytics/write-offs${reason ? `?reason=${encodeURIComponent(reason)}` : ""}`).then((response) => json<WriteOff[]>(response)),
    createWriteOff: (input: { reason: WriteOffReason; items: { productId: string; quantity: number; batchId?: string | null }[] }) =>
      request("/api/write-offs", { method: "POST", body: JSON.stringify(input) }).then((response) => json<{ count: number }>(response)),
    shiftCurrent: () => request("/api/shift/current").then((response) => json<ShiftCurrent>(response)),
    shiftOpen: (dishes: { dishId: string; portions: number }[]) =>
      request("/api/shift/open", { method: "POST", body: JSON.stringify({ dishes }) }).then((response) => json<ShiftCurrent>(response)),
    shiftClose: () => request("/api/shift/close", { method: "POST" }).then((response) => json<ShiftCurrent>(response)),
    shiftHistory: (dishId?: string) =>
      request(`/api/shift/history${dishId ? `?dishId=${dishId}` : ""}`).then((response) => json<ShiftHistoryEntry[]>(response)),
    shiftStop: (dishId: string, reason: string, note: string) =>
      request(`/api/shift/dishes/${dishId}/stop`, { method: "POST", body: JSON.stringify({ reason, note }) }).then((response) => json<StopEntry>(response)),
    shiftResume: (dishId: string) =>
      request(`/api/shift/dishes/${dishId}/resume`, { method: "POST" }).then((response) => json<StopEntry>(response)),
    shiftPortions: (dishId: string, delta: number) =>
      request(`/api/shift/dishes/${dishId}/portions`, { method: "POST", body: JSON.stringify({ delta }) }).then((response) => json<StopEntry>(response)),
    shiftPriority: (dishId: string, on: boolean) =>
      request(`/api/shift/dishes/${dishId}/priority`, { method: "POST", body: JSON.stringify({ on }) }).then((response) => json<StopEntry>(response)),
    deficits: () => request("/api/analytics/deficits").then((response) => json<Deficit[]>(response)),
    weeklyForecast: () => request("/api/analytics/weekly-forecast").then((response) => json<WeeklyForecastReport>(response)),
    spend: (days: number) => request(`/api/analytics/spend?days=${days}`).then((response) => json<SpendReport>(response)),
    shiftJournal: () => request("/api/analytics/shifts").then((response) => json<ShiftJournalRow[]>(response)),
    reports: () => request("/api/reports").then((response) => json<MoneyReport>(response)),
    kitchen: () => request("/api/kitchen").then((response) => json<KitchenBoard>(response)),
    markDish: (orderId: string, itemId: string, status: KitchenStatus, note: string) =>
      request(`/api/kitchen/orders/${orderId}/items/${itemId}`, {
        method: "POST",
        body: JSON.stringify({ status, note }),
      }).then((response) => json<Order>(response)),
    markBatch: (batchId: string, opened: boolean, note: string, writeOffQuantity: number | null) =>
      request(`/api/kitchen/batches/${batchId}`, {
        method: "POST",
        body: JSON.stringify({ opened, note, writeOffQuantity }),
      }).then((response) => json<unknown>(response)),
    purchaseRequest: (productIds?: string[]) =>
      request("/api/analytics/purchase-request", {
        method: "POST",
        body: JSON.stringify({ productIds: productIds ?? null }),
      }).then((response) => json<PurchaseDraft>(response)),
    simulateDelivery: () => request("/api/simulator/delivery", { method: "POST" }).then((response) => json<Delivery>(response)),
    async simulateOrder() {
      const response = await request("/api/simulator/order", { method: "POST" });
      const text = await response.text();
      const body = text ? (JSON.parse(text) as Order & { message?: string }) : null;
      if (response.status === 409 && body) {
        return body as Order;
      }
      if (!response.ok) {
        throw new ApiError(body?.message ?? "Имитация заказа не выполнена", response.status);
      }
      return body as Order;
    },
  };
}
