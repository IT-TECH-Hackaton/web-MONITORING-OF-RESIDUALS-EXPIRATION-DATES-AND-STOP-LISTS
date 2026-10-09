import { ApiError } from "@repo/contracts";
import { Redirect } from "expo-router";
import { useState } from "react";
import { Pressable, Text, TextInput, View } from "react-native";
import { useSession } from "../src/session";
import { colors, styles } from "../src/theme";

export default function LoginScreen() {
  const { session, client, baseUrl, setBaseUrl } = useSession();
  const [login, setLogin] = useState("waiter");
  const [password, setPassword] = useState("waiter123");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  if (session?.role === "Waiter") return <Redirect href="/(floor)/stop" />;

  async function submit() {
    setPending(true);
    setError(null);
    try {
      const next = await client.login(login.trim(), password);
      if (next.role !== "Waiter") {
        client.logout();
        setError("Этот вход для официанта. Менеджер работает в веб-консоли.");
      }
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "Не удалось войти");
    } finally {
      setPending(false);
    }
  }

  return (
    <View style={styles.screen}>
      <Text style={styles.title}>Зал</Text>
      <Text style={styles.muted}>Стоп-лист, заказ и блюда, которые стоит предложить гостю.</Text>
      <TextInput style={styles.input} value={baseUrl} onChangeText={setBaseUrl} autoCapitalize="none" placeholder="Адрес API" placeholderTextColor={colors.muted} />
      <TextInput style={styles.input} value={login} onChangeText={setLogin} autoCapitalize="none" placeholder="Логин" placeholderTextColor={colors.muted} />
      <TextInput style={styles.input} value={password} onChangeText={setPassword} secureTextEntry placeholder="Пароль" placeholderTextColor={colors.muted} />
      {error ? <Text style={styles.error}>{error}</Text> : <Text style={styles.muted}>Демо: waiter / waiter123</Text>}
      <Pressable style={styles.button} onPress={() => void submit()} disabled={pending}>
        <Text style={styles.buttonText}>{pending ? "Входим…" : "Войти"}</Text>
      </Pressable>
    </View>
  );
}
