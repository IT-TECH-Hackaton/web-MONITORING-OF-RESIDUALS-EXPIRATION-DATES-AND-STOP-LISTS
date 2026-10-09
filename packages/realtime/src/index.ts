import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

export const LiveEvents = {
  stockChanged: "stock.changed",
  batchExpiring: "batch.expiring",
  batchExpired: "batch.expired",
  stopListUpdated: "stoplist.updated",
  hotOffersUpdated: "hotoffers.updated",
  orderDecided: "order.decided",
  kitchenUpdated: "kitchen.updated",
} as const;

export type StockChangedPayload = { productIds: string[] };
export type BatchPayload = { batchIds: string[] };
export type OrderDecidedPayload = { orderId: string; status: string };

export function connectLive(baseUrl: string, accessToken: () => string): HubConnection {
  const root = baseUrl.replace(/\/$/, "");
  return new HubConnectionBuilder()
    .withUrl(`${root}/hubs/live`, { accessTokenFactory: accessToken })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
