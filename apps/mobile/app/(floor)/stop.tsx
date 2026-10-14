import { type StopEntry } from "@repo/contracts";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { StopWhyModal } from "../../src/StopWhy";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function StopScreen() {
  const { client } = useSession();
  const stop = useQuery({ queryKey: ["stop-list"], queryFn: () => client.stopList() });
  const [why, setWhy] = useState<StopEntry | null>(null);
  const stopped = stop.data?.filter((entry) => entry.isStopped) ?? [];
  const low = stop.data?.filter((entry) => entry.isLow) ?? [];

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Стоп</Text>
      <Text style={styles.muted}>Нажми блюдо — почему закрыто. Мало — заготовка на исходе, блюдо ещё в меню.</Text>
      {stop.isLoading ? <Text style={styles.muted}>Обновляем смену…</Text> : null}
      {stop.isError ? <Text style={styles.error}>Стоп-лист не загрузился.</Text> : null}
      <Text style={styles.section}>Стоп</Text>
      {stopped.length === 0 ? <Text style={styles.muted}>Ничего не закрыто.</Text> : null}
      {stopped.map((entry) => (
        <Pressable key={entry.dishId} style={styles.cardStopped} onPress={() => setWhy(entry)}>
          <View style={styles.row}>
            <Text style={styles.name}>{entry.dishName}</Text>
            <View style={styles.badgeStop}><Text style={styles.badgeStopText}>0</Text></View>
          </View>
          {entry.reason ? <Text style={styles.error}>{entry.reason}</Text> : null}
        </Pressable>
      ))}
      <Text style={styles.section}>Мало</Text>
      {low.length === 0 ? <Text style={styles.muted}>Нет блюд на исходе заготовки.</Text> : null}
      {low.map((entry) => (
        <View key={entry.dishId} style={styles.cardLow}>
          <View style={styles.row}>
            <Text style={styles.name}>{entry.dishName}</Text>
            <View style={styles.badgeLow}><Text style={styles.badgeLowText}>{entry.portionsPossible}</Text></View>
          </View>
          <Text style={styles.muted}>Порций на заготовке</Text>
        </View>
      ))}
      <StopWhyModal entry={why} onClose={() => setWhy(null)} />
    </ScrollView>
  );
}
