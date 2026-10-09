import { useQuery } from "@tanstack/react-query";
import { ScrollView, Text, View } from "react-native";
import { useSession } from "../../src/session";
import { styles } from "../../src/theme";

export default function TipsScreen() {
  const { client } = useSession();
  const offers = useQuery({ queryKey: ["hot-offers"], queryFn: () => client.hotOffers() });

  return (
    <ScrollView contentContainerStyle={styles.screen}>
      <Text style={styles.title}>Предложить</Text>
      <Text style={styles.muted}>Список сам обновляется, когда меняются остатки.</Text>
      {offers.data?.length === 0 ? <Text style={styles.muted}>Сейчас нет блюд, которыми можно спасти короткий срок.</Text> : null}
      {offers.data?.map((offer) => (
        <View key={offer.dishId} style={styles.card}>
          <View style={styles.badge}><Text style={styles.badgeText}>предложить</Text></View>
          <Text style={styles.name}>{offer.dishName}</Text>
          <Text style={styles.muted}>{offer.reason}</Text>
        </View>
      ))}
    </ScrollView>
  );
}
