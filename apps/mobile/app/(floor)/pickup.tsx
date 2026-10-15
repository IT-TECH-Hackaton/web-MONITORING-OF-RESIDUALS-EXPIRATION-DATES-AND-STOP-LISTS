import type { KitchenStatus } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { useSession } from "../../src/session";
import { colors, styles } from "../../src/theme";

const label: Record<KitchenStatus, string> = {
  Queued: "в очереди",
  Cooking: "готовится",
  Ready: "готово",
  Waste: "брак",
  Problem: "отказ",
};

export default function PickupScreen() {
  const { client, session } = useSession();
  const orders = useQuery({ queryKey: ["orders"], queryFn: () => client.orders() });
  const [mineOnly, setMineOnly] = useState(true);
  const accepted = (orders.data ?? [])
    .filter((order) => order.status === "Accepted")
    .filter((order) => !mineOnly || order.createdByLogin === session?.login)
    .sort((left, right) => left.createdAt.localeCompare(right.createdAt));

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Отдача</Text>
      <Text style={styles.muted}>Кухня отмечает готовность, список обновляется сам.</Text>
      <Pressable style={styles.ghost} onPress={() => setMineOnly((value) => !value)}>
        <Text style={styles.ghostText}>{mineOnly ? "Только мои" : "Все заказы"}</Text>
      </Pressable>
      {accepted.length === 0 ? <Text style={styles.muted}>Готовить пока нечего.</Text> : null}
      {accepted.map((order) => {
        const ready = order.items.length > 0 && order.items.every((item) => item.kitchenStatus === "Ready");
        return (
          <View key={order.id} style={ready ? styles.card : styles.cardStopped}>
            <Text style={styles.name}>{ready ? "Можно забирать" : "Ещё готовят"}</Text>
            <Text style={styles.muted}>{order.createdByLogin}</Text>
            {order.items.map((item) => (
              <Text key={item.id} style={{ color: item.kitchenStatus === "Ready" ? colors.ok : colors.text, fontSize: 16 }}>
                {item.dishName} × {item.portions} · {label[item.kitchenStatus]}
                {item.kitchenNote ? ` · ${item.kitchenNote}` : ""}
              </Text>
            ))}
          </View>
        );
      })}
    </ScrollView>
  );
}