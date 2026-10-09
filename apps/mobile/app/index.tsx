import { Redirect } from "expo-router";
import { ActivityIndicator, Text, View } from "react-native";
import { useSession } from "../src/session";
import { colors, styles } from "../src/theme";

export default function Index() {
  const { ready, session, client } = useSession();
  if (!ready) return <ActivityIndicator color={colors.copper} style={{ flex: 1, backgroundColor: colors.background }} />;
  if (!session) return <Redirect href="/login" />;
  if (session.role !== "Waiter") {
    return (
      <View style={styles.screen}>
        <Text style={styles.title}>Это приложение зала</Text>
        <Text style={styles.muted}>Аккаунт {session.login} работает в веб-консоли менеджера.</Text>
        <Text style={styles.buttonText} onPress={() => client.logout()}>Выйти</Text>
      </View>
    );
  }
  return <Redirect href="/(floor)/stop" />;
}
