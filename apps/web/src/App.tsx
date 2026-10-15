import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarTrigger,
} from "@/components/ui/sidebar";
import { TooltipProvider } from "@/components/ui/tooltip";
import { Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { CartProvider } from "./cart";
import { AnalyticsPage } from "./pages/Analytics";
import { DashboardPage } from "./pages/Dashboard";
import { DeliveriesPage, DeliveryPage } from "./pages/Deliveries";
import { FloorMenuPage, FloorOrderPage, FloorShell } from "./pages/Floor";
import { IntakePage } from "./pages/Intake";
import { DishesPage } from "./pages/Dishes";
import { LoginPage } from "./pages/Login";
import { KitchenChatPage, useChatUnread } from "./chat";
import { KitchenIntakePage } from "./pages/KitchenIntake";
import { KitchenPage } from "./pages/Kitchen";
import { KitchenOrdersPage } from "./pages/KitchenOrders";
import { SupplyPage } from "./pages/Supply";
import { OffersPage } from "./pages/Offers";
import { OrdersPage } from "./pages/Orders";
import { ReportsPage } from "./pages/Reports";
import { StockPage, StockProductPage } from "./pages/Stock";
import { TeamPage } from "./pages/Team";
import { LiveNotices } from "./notices";
import { useSession } from "./session";
import { ThemeToggle } from "./theme";

const managerLinks = [
  ["/", "Сводка"],
  ["/stock", "Склад"],
  ["/deliveries", "Поставки"],
  ["/dishes", "Блюда"],
  ["/orders", "Заказы"],
  ["/analytics", "Аналитика"],
  ["/reports", "Отчёты"],
  ["/team", "Команда"],
];

const kitchenLinks = [["/kitchen", "Смена"], ["/kitchen/orders", "Заказы"], ["/kitchen/intake", "Приёмка"], ["/kitchen/chat", "Чат"]];
const supplyLinks = [["/supply", "Предложение"]];

function Shell() {
  const { session, client } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const unread = useChatUnread();
  if (!session) return <Navigate to="/login" replace />;
  if (session.role === "Waiter") return <Navigate to="/floor" replace />;
  if (session.role === "Kitchen" && !location.pathname.startsWith("/kitchen")) return <Navigate to="/kitchen" replace />;
  if (session.role === "Supply" && location.pathname !== "/supply") return <Navigate to="/supply" replace />;

  const links = session.role === "Kitchen" ? kitchenLinks : session.role === "Supply" ? supplyLinks : managerLinks;
  const badge = session.role === "Kitchen" ? "Смена кухни" : session.role === "Supply" ? "Предложение" : "Стоп, сроки, склад";
  return (
    <TooltipProvider>
      <SidebarProvider>
        <Sidebar collapsible="offcanvas">
          <SidebarHeader>
            <div className="flex flex-col gap-2 px-2 py-1">
              <strong className="text-sm">Остатки</strong>
              <Badge variant="secondary">{badge}</Badge>
            </div>
          </SidebarHeader>
          <SidebarContent>
            <SidebarGroup>
              <SidebarGroupContent>
                <SidebarMenu>
                  {links.map(([to, label]) => {
                    const exact = to === "/" || to === "/kitchen";
                    const active = exact ? location.pathname === to : location.pathname.startsWith(to);
                    const badge = unread > 0 && (to === "/kitchen/chat" || to === "/team");
                    return (
                      <SidebarMenuItem key={to}>
                        <SidebarMenuButton
                          size="lg"
                          isActive={active}
                          render={<NavLink to={to} end={exact} />}
                        >
                          <span className="flex w-full items-center justify-between gap-2">
                            {label}
                            {badge ? <span className="chat-unread">{unread > 9 ? "9+" : unread}</span> : null}
                          </span>
                        </SidebarMenuButton>
                      </SidebarMenuItem>
                    );
                  })}
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>
          </SidebarContent>
          <SidebarFooter>
            <div className="flex items-center justify-between gap-2 px-2">
              <span className="text-xs text-muted-foreground">{session.login}</span>
              <ThemeToggle compact />
            </div>
            <Button variant="outline" onClick={() => { client.logout(); navigate("/login"); }}>Выйти</Button>
          </SidebarFooter>
        </Sidebar>
        <SidebarInset>
          <header className="flex h-12 items-center gap-2 border-b px-3 md:hidden">
            <SidebarTrigger />
            <span className="text-sm font-medium">Остатки</span>
          </header>
          <div className="main">
            <LiveNotices />
            <Outlet />
          </div>
        </SidebarInset>
      </SidebarProvider>
    </TooltipProvider>
  );
}

function FloorGate() {
  const { session } = useSession();
  if (!session) return <Navigate to="/login" replace />;
  if (session.role !== "Waiter") return <Navigate to="/" replace />;
  return (
    <CartProvider>
      <FloorShell />
    </CartProvider>
  );
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/floor" element={<FloorGate />}>
        <Route index element={<FloorMenuPage />} />
        <Route path="order" element={<FloorOrderPage />} />
      </Route>
      <Route element={<Shell />}>
        <Route index element={<DashboardPage />} />
        <Route path="kitchen" element={<KitchenPage />} />
        <Route path="kitchen/orders" element={<KitchenOrdersPage />} />
        <Route path="kitchen/intake" element={<KitchenIntakePage />} />
        <Route path="kitchen/chat" element={<KitchenChatPage />} />
        <Route path="supply" element={<SupplyPage />} />
        <Route path="deliveries" element={<DeliveriesPage />} />
        <Route path="intake" element={<IntakePage />} />
        <Route path="deliveries/:id" element={<DeliveryPage />} />
        <Route path="stock" element={<StockPage />} />
        <Route path="stock/:productId" element={<StockProductPage />} />
        <Route path="dishes" element={<DishesPage />} />
        <Route path="offers" element={<OffersPage />} />
        <Route path="orders" element={<OrdersPage />} />
        <Route path="analytics" element={<AnalyticsPage />} />
        <Route path="reports" element={<ReportsPage />} />
        <Route path="team" element={<TeamPage />} />
      </Route>
    </Routes>
  );
}
