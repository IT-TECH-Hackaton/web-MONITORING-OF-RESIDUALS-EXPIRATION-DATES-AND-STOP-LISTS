export type Unit = "G" | "Ml" | "Pcs";
export type BatchStatus = "Active" | "Depleted" | "Expired";
export type OrderStatus = "Accepted" | "Rejected";
export type Freshness = "Ok" | "Expiring" | "Expired" | "Empty";
export type MovementType = "Receipt" | "OrderWriteOff" | "ExpiryWriteOff" | "KitchenWriteOff";
export type Role = "Manager" | "Waiter" | "Kitchen";
export type KitchenStatus = "Queued" | "Cooking" | "Ready";

export type Session = {
  accessToken: string;
  refreshToken: string;
  role: Role;
  login: string;
  userId: string;
};

export type Shortage = {
  productId: string;
  productName: string;
  required: number;
  available: number;
  deficit: number;
};

export type OrderLine = {
  id: string;
  dishId: string;
  dishName: string;
  portions: number;
  kitchenStatus: KitchenStatus;
  kitchenNote: string | null;
};

export type Order = {
  id: string;
  createdAt: string;
  createdByLogin: string;
  status: OrderStatus;
  rejectionReason: string | null;
  items: OrderLine[];
  shortages: Shortage[];
  tableNumber: string | null;
};

export type Batch = {
  id: string;
  deliveryId: string;
  deliveryNumber: string;
  productId: string;
  productName: string;
  unit: Unit;
  quantityInitial: number;
  quantityRemaining: number;
  receivedAt: string;
  expiresAt: string;
  hoursLeft: number;
  status: BatchStatus;
  freshness: Freshness;
  certificateNumber?: string | null;
  certificateIssuer?: string | null;
  certificateExpiresAt?: string | null;
};

export type Delivery = {
  id: string;
  number: string;
  createdAt: string;
  supplier?: string | null;
  batches: Batch[];
};

export type QrLineVerdict = {
  productName: string;
  certificateNumber: string | null;
  ok: boolean;
  reason: string | null;
};

export type QrIntakeResult = {
  accepted: boolean;
  summary: string;
  lines: QrLineVerdict[];
  delivery: Delivery | null;
};

export type DeliveryLineInput = {
  productId: string;
  name: string;
  unit: Unit;
  quantity: number;
  expiresAt?: string | null;
  shelfLifeHours?: number | null;
};

export type StockItem = {
  productId: string;
  name: string;
  unit: Unit;
  available: number;
  nearestExpiry: string | null;
  hoursLeft: number | null;
  freshness: Freshness;
  purchasePrice: number;
};

export type MoneyReport = {
  purchaseRub: number;
  revenueRub: number;
  costRub: number;
  wasteRub: number;
  netRub: number;
  staff: { name: string; role: string; purchaseRub: number; deliveries: number; salesRub: number; orders: number }[];
  purchases: { productId: string; productName: string; unit: Unit; quantity: number; rub: number }[];
  dishes: { dishName: string; salePrice: number; portions: number; revenueRub: number; costRub: number; netRub: number }[];
};

export type Product = {
  id: string;
  name: string;
  unit: Unit;
  isActive: boolean;
  purchasePrice: number;
};

export type DishIngredient = {
  productId: string;
  productName: string;
  unit: Unit;
  quantity: number;
};

export type Dish = {
  id: string;
  name: string;
  isActive: boolean;
  ingredients: DishIngredient[];
  salePrice: number;
};

export type StopReason = "OutOfProduct" | "Waste" | "Quality" | "WontMakeIt" | "Equipment";

export type StopEntry = {
  dishId: string;
  dishName: string;
  portionsPossible: number;
  isStopped: boolean;
  isCandidate: boolean;
  isLow: boolean;
  isPriority: boolean;
  reason: string | null;
  candidateReason: string | null;
  stopReason: StopReason | null;
  hasLimit: boolean;
};

export type ShiftHistoryEntry = {
  id: string;
  dishId: string;
  at: string;
  actorLogin: string;
  text: string;
};

export type HotOffer = {
  dishId: string;
  dishName: string;
  score: number;
  reason: string;
};

export type ExpiryTotal = {
  productId: string;
  productName: string;
  unit: Unit;
  quantity: number;
};

export type Forecast = {
  dishName: string;
  productName: string;
  hoursUntilStop: number;
};

export type WeeklyForecastItem = {
  productId: string;
  productName: string;
  unit: Unit;
  consumedThisWeek: number;
  projectedNeed: number;
  onHand: number;
  shortfall: number;
  dishes: string[];
};

export type WeeklyForecastReport = {
  weekStart: string;
  elapsedDays: number;
  ordersThisWeek: number;
  items: WeeklyForecastItem[];
};

export type PurchaseDraft = {
  payload: string;
  number: string;
  itemCount: number;
};

export type KitchenBatchCard = {
  batchId: string;
  productName: string;
  unit: Unit;
  deliveryNumber: string;
  remaining: number;
  hoursLeft: number;
  openedAt: string | null;
  note: string | null;
};

export type KitchenBoard = {
  batches: KitchenBatchCard[];
};

export type Dashboard = {
  expiring: Batch[];
  stoppedCount: number;
  expiryWriteOffToday: ExpiryTotal[];
  acceptedOrdersToday: number;
  forecasts: Forecast[];
  weeklyForecast: WeeklyForecastReport;
};

export type WriteOff = {
  productId: string;
  productName: string;
  unit: Unit;
  type: MovementType;
  reason: string | null;
  quantity: number;
};

export type WriteOffReason = "Expiry" | "Waste" | "Spoilage" | "Training" | "StaffMeal";

export type Deficit = {
  productId: string;
  productName: string;
  unit: Unit;
  rejectionCount: number;
  totalDeficit: number;
  available: number;
};

export class ApiError extends Error {
  status: number;
  order?: Order;

  constructor(message: string, status: number, order?: Order) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.order = order;
  }
}
