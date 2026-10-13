import { useQuery } from "@tanstack/react-query";
import { Pressable, ScrollView, Text, View } from "react-native";
import { useCart } from "../../src/cart";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function TipsScreen() {
  const { client } = useSession();
  const cart = useCart();
  const priority = useQuery({ queryKey: ["priority"], queryFn: () => client.priority() });

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Предложить</Text>
      <Text style={styles.muted}>Только то, что шеф подтвердил на смене. Подсказка склада сюда не попадает.</Text>
      {priority.isLoading ? <Text style={styles.muted}>Смотрим смену…</Text> : null}
      {priority.isError ? <Text style={styles.error}>Список шефа не загрузился.</Text> : null}
      {priority.data?.length === 0 ? <Text style={styles.muted}>Шеф ещё не отметил, что предлагать.</Text> : null}
      {priority.data?.map((entry) => (
        <View key={entry.dishId} style={styles.card}>
          <View style={styles.row}>
            <Text style={styles.name}>{entry.dishName}</Text>
            <View style={styles.badge}><Text style={styles.badgeText}>предложить</Text></View>
          </View>
          <Text style={styles.muted}>
            {entry.hasLimit ? `На заготовке ${entry.portionsPossible}` : "Лимит заготовки не задан"}
          </Text>
          <Pressable style={styles.button} onPress={() => cart.add(entry.dishId, entry.dishName)}>
            <Text style={styles.buttonText}>В заказ</Text>
          </Pressable>
        </View>
      ))}
    </ScrollView>
  );
}
