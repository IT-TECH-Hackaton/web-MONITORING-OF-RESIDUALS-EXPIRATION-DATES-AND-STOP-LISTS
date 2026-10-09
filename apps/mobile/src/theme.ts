import { StyleSheet } from "react-native";

export const colors = {
  background: "#171310",
  card: "#2a231c",
  cardMuted: "#3a3028",
  text: "#f7f0e7",
  muted: "#cbb8a4",
  copper: "#e08a45",
  danger: "#e07a72",
  ok: "#8dcea8",
  line: "#4a3d33",
};

export const styles = StyleSheet.create({
  screen: { flexGrow: 1, backgroundColor: colors.background, padding: 16, paddingBottom: 28, gap: 12 },
  title: { color: colors.text, fontSize: 34, fontWeight: "700", letterSpacing: -0.6 },
  muted: { color: colors.muted, fontSize: 15, lineHeight: 22 },
  card: {
    backgroundColor: colors.card,
    borderRadius: 20,
    padding: 16,
    gap: 10,
    borderWidth: 1,
    borderColor: colors.line,
  },
  cardStopped: {
    backgroundColor: "#3a2422",
    borderRadius: 20,
    padding: 16,
    gap: 10,
    borderWidth: 1,
    borderColor: "#6e3a36",
    borderLeftWidth: 4,
    borderLeftColor: colors.danger,
  },
  name: { color: colors.text, fontSize: 22, fontWeight: "700", flexShrink: 1 },
  row: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 12 },
  badge: { backgroundColor: colors.copper, borderRadius: 999, paddingHorizontal: 10, paddingVertical: 5 },
  badgeText: { color: "#2a160c", fontWeight: "700", fontSize: 12 },
  button: { backgroundColor: colors.copper, borderRadius: 14, minHeight: 52, alignItems: "center", justifyContent: "center", paddingHorizontal: 16 },
  buttonText: { color: "#2a160c", fontSize: 17, fontWeight: "700" },
  ghost: { borderRadius: 14, minHeight: 48, minWidth: 48, alignItems: "center", justifyContent: "center", backgroundColor: colors.cardMuted },
  ghostText: { color: colors.text, fontSize: 22, fontWeight: "700" },
  input: { backgroundColor: colors.card, color: colors.text, borderRadius: 14, paddingHorizontal: 14, minHeight: 52, fontSize: 16, borderWidth: 1, borderColor: colors.line },
  error: { color: colors.danger, fontSize: 15, lineHeight: 22 },
});