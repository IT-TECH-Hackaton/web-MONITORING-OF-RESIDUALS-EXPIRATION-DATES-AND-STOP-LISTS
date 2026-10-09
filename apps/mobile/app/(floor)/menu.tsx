import { useQuery } from "@tanstack/react-query";
import { Pressable, ScrollView, Text, View } from "react-native";
import { useCart } from "../../src/cart";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function MenuScreen() {
  const { client } = useSession();
  const cart = useCart();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });
  const hot = new Set(offers.data?.map((offer) => offer.dishId));

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Меню</Text>
      {stop.data?.map((entry) => (
        <View key={entry.dishId} style={entry.isStopped ? styles.cardStopped : styles.card}>
          <View style={styles.row}>
            <Text style={styles.name}>{entry.dishName}</Text>
            {hot.has(entry.dishId) && !entry.isStopped ? <View style={styles.badge}><Text style={styles.badgeText}>предложить</Text></View> : null}
          </View>
          <Text style={styles.muted}>
            {entry.isStopped ? entry.reason : `Хватит на ${entry.portionsPossible} порц.`}
          </Text>
          {!entry.isStopped ? (
            <Pressable style={styles.button} onPress={() => cart.add(entry.dishId, entry.dishName)}>
              <Text style={styles.buttonText}>В заказ</Text>
            </Pressable>
          ) : null}
        </View>
      ))}
    </ScrollView>
  );
}
