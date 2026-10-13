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
];

const kitchenLinks = [["/kitchen", "Смена"]];

function Shell() {
  const { session, client } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  if (!session) return <Navigate to="/login" replace />;
  if (session.role === "Waiter") return <Navigate to="/floor" replace />;
  if (session.role === "Kitchen" && location.pathname !== "/kitchen") return <Navigate to="/kitchen" replace />;

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <strong>Остатки</strong>
          <span>{session.role === "Kitchen" ? "Смена кухни" : "Стоп, сроки, склад"}</span>
        </div>
        <nav className="nav">
          {(session.role === "Kitchen" ? kitchenLinks : managerLinks).map(([to, label]) => (
            <NavLink key={to} to={to} end={to === "/"}>
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-foot">
          <span>{session.login}</span>
          <div className="sidebar-foot-actions">
            <button type="button" className="sidebar-logout" onClick={() => { client.logout(); navigate("/login"); }}>Выйти</button>
            <ThemeToggle />
          </div>
        </div>
      </aside>
      <main className="main">
        <Outlet />
      </main>
    </div>
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
      </Route>
    </Routes>
  );
}
