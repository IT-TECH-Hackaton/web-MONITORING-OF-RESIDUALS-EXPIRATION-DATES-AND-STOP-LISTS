import { ApiError, newId } from "@repo/contracts";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { useCart } from "../../src/cart";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function OrderScreen() {
  const { client } = useSession();
  const cart = useCart();
  const queryClient = useQueryClient();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const availability = new Map(stop.data?.map((entry) => [entry.dishId, entry]));
  const blocked = cart.lines.some((line) => {
    const entry = availability.get(line.dishId);
    return entry != null && (entry.isStopped || line.portions > entry.portionsPossible);
  });

  async function submit() {
    if (blocked) return;
    setPending(true);
    setError(null);
    setDone(null);
    try {
      await client.placeOrder({
        id: newId(),
        items: cart.lines.map((line) => ({ dishId: line.dishId, portions: line.portions })),
      });
      cart.clear();
      setDone("Заказ принят, продукты списаны.");
      await queryClient.invalidateQueries({ queryKey: ["stop-list"] });
      await queryClient.invalidateQueries({ queryKey: ["hot-offers"] });
    } catch (caught) {
      if (caught instanceof ApiError && caught.order) {
        const lines = caught.order.shortages.map((item) => `${item.productName}: не хватает ${item.deficit}`).join("\n");
        setError(lines || caught.message);
      } else {
        setError(caught instanceof Error ? caught.message : "Заказ не отправлен");
      }
    } finally {
      setPending(false);
    }
  }

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Заказ</Text>
      {cart.lines.length === 0 ? <Text style={styles.muted}>Корзина пустая. Блюда из стоп-листа сюда не попадают.</Text> : null}
      {cart.lines.map((line) => {
        const entry = availability.get(line.dishId);
        const stopped = entry?.isStopped === true;
        const over = entry != null && line.portions > entry.portionsPossible;
        return (
          <View key={line.dishId} style={stopped || over ? styles.cardStopped : styles.card}>
            <Text style={styles.name}>{line.dishName}</Text>
            {stopped ? <Text style={styles.error}>{entry?.reason}</Text> : null}
            {!stopped && over ? <Text style={styles.muted}>Хватит на {entry?.portionsPossible} порц. Уменьшите заказ.</Text> : null}
            <View style={styles.row}>
              <Pressable style={styles.ghost} onPress={() => cart.change(line.dishId, line.portions - 1)}><Text style={styles.ghostText}>−</Text></Pressable>
              <Text style={styles.name}>{line.portions}</Text>
              <Pressable
                style={styles.ghost}
                disabled={entry != null && line.portions >= entry.portionsPossible}
                onPress={() => cart.change(line.dishId, line.portions + 1)}
              >
                <Text style={styles.ghostText}>+</Text>
              </Pressable>
            </View>
          </View>
        );
      })}
      {blocked ? <Text style={styles.error}>В корзине есть блюдо из стоп-листа или порций больше, чем можно приготовить.</Text> : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
      {done ? <Text style={styles.muted}>{done}</Text> : null}
      {cart.lines.length > 0 ? (
        <Pressable style={styles.button} onPress={() => void submit()} disabled={pending || blocked}>
          <Text style={styles.buttonText}>{pending ? "Отправляем…" : "Отправить заказ"}</Text>
        </Pressable>
      ) : null}
    </ScrollView>
  );
}
