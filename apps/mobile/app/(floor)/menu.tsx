import { type StopEntry } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { StopWhyModal } from "../../src/StopWhy";
import { useCart } from "../../src/cart";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function MenuScreen() {
  const { client } = useSession();
  const cart = useCart();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [why, setWhy] = useState<StopEntry | null>(null);
  const dishes = stop.data ?? [];
  const shiftClosed = dishes.length > 0 && dishes.every((entry) => entry.isStopped && entry.reason === "Смена закрыта");

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Меню</Text>
      {shiftClosed ? <Text style={styles.muted}>Смена закрыта. Заказы не принимаются.</Text> : null}
      {dishes.map((entry) => (
        <Pressable
          key={entry.dishId}
          style={entry.isStopped ? styles.cardStopped : styles.card}
          onPress={() => {
            if (entry.isStopped) setWhy(entry);
          }}
        >
          <View style={styles.row}>
            <Text style={styles.name}>{entry.dishName}</Text>
            {entry.isPriority && !entry.isStopped ? <View style={styles.badge}><Text style={styles.badgeText}>предложить</Text></View> : null}
          </View>
          <Text style={styles.muted}>
            {entry.isStopped
              ? entry.reason
              : entry.hasLimit
                ? (entry.isLow ? `Мало · ${entry.portionsPossible} порц.` : `Хватит на ${entry.portionsPossible} порц.`)
                : "Лимит заготовки не задан"}
          </Text>
          {!entry.isStopped ? (
            <Pressable
              style={styles.button}
              onPress={() => {
                const have = cart.lines.find((line) => line.dishId === entry.dishId)?.portions ?? 0;
                if (entry.hasLimit && have + 1 > entry.portionsPossible) return;
                cart.add(entry.dishId, entry.dishName);
              }}
            >
              <Text style={styles.buttonText}>В заказ</Text>
            </Pressable>
          ) : null}
        </Pressable>
      ))}
      <StopWhyModal entry={why} onClose={() => setWhy(null)} />
    </ScrollView>
  );
}
