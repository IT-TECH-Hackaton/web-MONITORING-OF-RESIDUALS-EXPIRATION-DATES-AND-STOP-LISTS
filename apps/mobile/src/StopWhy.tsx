import { stopWhy, type StopEntry } from "@repo/contracts";
import { Modal, Pressable, Text } from "react-native";
import { styles } from "./theme";

export function StopWhyModal({
  entry,
  onClose,
}: {
  entry: StopEntry | null;
  onClose: () => void;
}) {
  if (!entry) return null;
  const why = stopWhy(entry);
  return (
    <Modal visible transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.modalBack} onPress={onClose}>
        <Pressable style={styles.modalCard} onPress={(event) => event.stopPropagation()}>
          <Text style={styles.section}>Стоп-лист</Text>
          <Text style={styles.name}>{entry.dishName}</Text>
          <Text style={styles.muted}>{why.source}</Text>
          {why.category ? <Text style={styles.name}>{why.category}</Text> : null}
          <Text style={styles.error}>{why.detail}</Text>
          <Pressable style={styles.button} onPress={onClose}>
            <Text style={styles.buttonText}>Понятно</Text>
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
