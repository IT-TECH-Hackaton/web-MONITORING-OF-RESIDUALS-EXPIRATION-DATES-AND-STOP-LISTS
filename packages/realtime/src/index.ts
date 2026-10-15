import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

export const LiveEvents = {
  stockChanged: "stock.changed",
  batchExpiring: "batch.expiring",
  batchExpired: "batch.expired",
  stopListUpdated: "stoplist.updated",
  hotOffersUpdated: "hotoffers.updated",
  orderDecided: "order.decided",
  orderSpoiled: "order.spoiled",
  kitchenUpdated: "kitchen.updated",
  shiftUpdated: "shift.updated",
  chatMessage: "chat.message",
} as const;

export type StockChangedPayload = { productIds: string[] };
export type BatchPayload = { batchIds: string[] };
export type OrderDecidedPayload = { orderId: string; status: string };

/** Не вызывает stop(), пока negotiate не закончился: иначе клиент пишет «stopped during negotiation». */
export function bindLive(connection: HubConnection): () => void {
  let alive = true;
  void connection.start().then(() => {
    if (!alive) void connection.stop();
  }).catch(() => undefined);
  return () => {
    alive = false;
    if (connection.state === HubConnectionState.Connected) {
      void connection.stop();
    }
  };
}

export function connectLive(baseUrl: string, accessToken: () => string): HubConnection {
  const root = baseUrl.replace(/\/$/, "");
  return new HubConnectionBuilder()
    .withUrl(`${root}/hubs/live`, { accessTokenFactory: accessToken })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
