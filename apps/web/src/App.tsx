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
import { KitchenPage } from "./pages/Kitchen";
import { OffersPage } from "./pages/Offers";
import { OrdersPage } from "./pages/Orders";
import { ReportsPage } from "./pages/Reports";
import { StockPage, StockProductPage } from "./pages/Stock";
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
];

const kitchenLinks = [["/kitchen", "Смена"]];

function Shell() {
  const { session, client } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  if (!session) return <Navigate to="/login" replace />;
  if (session.role === "Waiter") return <Navigate to="/floor" replace />;
  if (session.role === "Kitchen" && location.pathname !== "/kitchen") return <Navigate to="/kitchen" replace />;

  const links = session.role === "Kitchen" ? kitchenLinks : managerLinks;
  return (
    <TooltipProvider>
      <SidebarProvider>
        <Sidebar collapsible="offcanvas">
          <SidebarHeader>
            <div className="flex flex-col gap-2 px-2 py-1">
              <strong className="text-sm">Остатки</strong>
              <Badge variant="secondary">{session.role === "Kitchen" ? "Смена кухни" : "Стоп, сроки, склад"}</Badge>
            </div>
          </SidebarHeader>
          <SidebarContent>
            <SidebarGroup>
              <SidebarGroupContent>
                <SidebarMenu>
                  {links.map(([to, label]) => {
                    const active = to === "/" ? location.pathname === "/" : location.pathname.startsWith(to);
                    return (
                      <SidebarMenuItem key={to}>
                        <SidebarMenuButton
                          size="lg"
                          isActive={active}
                          render={<NavLink to={to} end={to === "/"} />}
                        >
                          {label}
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
      </Route>
    </Routes>
  );
}
