import { useQuery } from "@tanstack/react-query";
import { ScrollView, Text, View } from "react-native";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function StopScreen() {
  const { client } = useSession();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const stopped = stop.data?.filter((entry) => entry.isStopped) ?? [];

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Стоп-лист</Text>
      <Text style={styles.muted}>{stopped.length === 0 ? "Сейчас можно готовить всё меню." : "Эти блюда гостю не предлагаем."}</Text>
      {stop.isLoading ? <Text style={styles.muted}>Обновляем остатки…</Text> : null}
      {stopped.map((entry) => (
        <View key={entry.dishId} style={styles.cardStopped}>
          <Text style={styles.name}>{entry.dishName}</Text>
          <Text style={styles.error}>{entry.reason}</Text>
        </View>
      ))}
    </ScrollView>
  );
}
