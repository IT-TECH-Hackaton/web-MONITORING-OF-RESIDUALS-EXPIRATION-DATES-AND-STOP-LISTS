import { Navigate, NavLink, Outlet, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { AnalyticsPage } from "./pages/Analytics";
import { DashboardPage } from "./pages/Dashboard";
import { DeliveriesPage, DeliveryPage } from "./pages/Deliveries";
import { IntakePage } from "./pages/Intake";
import { DishesPage } from "./pages/Dishes";
import { LoginPage } from "./pages/Login";
import { KitchenPage } from "./pages/Kitchen";
import { OffersPage } from "./pages/Offers";
import { OrdersPage } from "./pages/Orders";
import { StockPage, StockProductPage } from "./pages/Stock";
import { useSession } from "./session";

const managerLinks = [
  ["/", "Сводка"],
  ["/kitchen", "Кухня"],
  ["/deliveries", "Поставки"],
  ["/intake", "Приёмка QR"],
  ["/stock", "Склад"],
  ["/dishes", "Блюда"],
  ["/offers", "Горячие"],
  ["/orders", "Заказы"],
  ["/analytics", "Аналитика"],
];

const kitchenLinks = [["/kitchen", "Кухня"]];

function Shell() {
  const { session, client } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  if (!session) return <Navigate to="/login" replace />;
  if (session.role === "Kitchen" && location.pathname !== "/kitchen") return <Navigate to="/kitchen" replace />;
  if (session.role === "Waiter") {
    return (
      <main className="login-wrap">
        <section className="login-card">
          <h1>Это консоль менеджера</h1>
          <p>Аккаунт {session.login} открывает зал в мобильном приложении.</p>
          <button className="btn" onClick={() => { client.logout(); navigate("/login"); }}>Выйти</button>
        </section>
      </main>
    );
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <strong>Остатки</strong>
          <span>{session.role === "Kitchen" ? "экран кухни" : "сроки, стоп-лист, партии"}</span>
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
          <button onClick={() => { client.logout(); navigate("/login"); }}>Выйти</button>
        </div>
      </aside>
      <main className="main">
        <Outlet />
      </main>
    </div>
  );
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
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
