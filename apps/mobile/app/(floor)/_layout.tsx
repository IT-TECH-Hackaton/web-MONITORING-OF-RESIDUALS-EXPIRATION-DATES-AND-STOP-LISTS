import { Redirect, Tabs } from "expo-router";
import { useSession } from "../../src/session";
import { colors } from "../../src/theme";

export default function FloorLayout() {
  const { ready, session } = useSession();
  if (!ready) return null;
  if (!session || session.role !== "Waiter") return <Redirect href="/login" />;

  return (
    <Tabs
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerTintColor: colors.text,
        headerShadowVisible: false,
        tabBarStyle: { backgroundColor: colors.card, borderTopColor: colors.line, height: 64, paddingBottom: 8 },
        tabBarActiveTintColor: colors.copper,
        tabBarInactiveTintColor: colors.muted,
      }}
    >
      <Tabs.Screen name="stop" options={{ title: "Стоп" }} />
      <Tabs.Screen name="menu" options={{ title: "Меню" }} />
      <Tabs.Screen name="order" options={{ title: "Заказ" }} />
      <Tabs.Screen name="pickup" options={{ href: null }} />
      <Tabs.Screen name="tips" options={{ title: "Советы" }} />
    </Tabs>
  );
}
