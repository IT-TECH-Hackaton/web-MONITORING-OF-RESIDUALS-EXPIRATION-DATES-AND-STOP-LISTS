import { newId } from "@repo/contracts";
import { createContext, useContext, useMemo, useState, type ReactNode } from "react";

export type DishLine = {
  kind: "dish";
  dishId: string;
  dishName: string;
  portions: number;
};

export type NoteLine = {
  kind: "note";
  id: string;
  text: string;
};

export type CartLine = DishLine | NoteLine;

type CartStore = {
  lines: CartLine[];
  table: string;
  count: number;
  setTable: (value: string) => void;
  add: (dishId: string, dishName: string) => void;
  addNote: (text: string) => void;
  change: (dishId: string, portions: number) => void;
  removeNote: (id: string) => void;
  clear: () => void;
};

const CartContext = createContext<CartStore | null>(null);

export function CartProvider({ children }: { children: ReactNode }) {
  const [lines, setLines] = useState<CartLine[]>([]);
  const [table, setTable] = useState("");
  const store = useMemo<CartStore>(() => ({
    lines,
    table,
    count: lines.reduce((sum, line) => sum + (line.kind === "dish" ? line.portions : 1), 0),
    setTable,
    add: (dishId, dishName) => {
      setLines((current) => {
        const existing = current.find((line) => line.kind === "dish" && line.dishId === dishId);
        if (!existing) return [...current, { kind: "dish", dishId, dishName, portions: 1 }];
        return current.map((line) =>
          line.kind === "dish" && line.dishId === dishId ? { ...line, portions: line.portions + 1 } : line,
        );
      });
    },
    addNote: (text) => {
      const note = text.trim();
      if (!note) return;
      setLines((current) => {
        const same = current.find((line) => line.kind === "note" && line.text.toLocaleLowerCase("ru") === note.toLocaleLowerCase("ru"));
        if (same) return current;
        return [...current, { kind: "note", id: newId(), text: note }];
      });
    },
    change: (dishId, portions) => {
      setLines((current) =>
        portions <= 0
          ? current.filter((line) => !(line.kind === "dish" && line.dishId === dishId))
          : current.map((line) => (line.kind === "dish" && line.dishId === dishId ? { ...line, portions } : line)),
      );
    },
    removeNote: (id) => setLines((current) => current.filter((line) => !(line.kind === "note" && line.id === id))),
    clear: () => {
      setLines([]);
      setTable("");
    },
  }), [lines, table]);

  return <CartContext.Provider value={store}>{children}</CartContext.Provider>;
}

export function useCart() {
  const value = useContext(CartContext);
  if (!value) throw new Error("Корзина не инициализирована");
  return value;
}
