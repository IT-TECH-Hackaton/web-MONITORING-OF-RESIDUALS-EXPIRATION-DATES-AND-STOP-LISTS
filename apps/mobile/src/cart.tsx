import AsyncStorage from "@react-native-async-storage/async-storage";
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

const key = "floor-cart";

export type CartLine = {
  dishId: string;
  dishName: string;
  portions: number;
};

type CartStore = {
  lines: CartLine[];
  add: (dishId: string, dishName: string) => void;
  change: (dishId: string, portions: number) => void;
  clear: () => void;
};

const CartContext = createContext<CartStore | null>(null);

export function CartProvider({ children }: { children: ReactNode }) {
  const [lines, setLines] = useState<CartLine[]>([]);

  useEffect(() => {
    void AsyncStorage.getItem(key).then((raw) => {
      if (!raw) return;
      try {
        setLines(JSON.parse(raw) as CartLine[]);
      } catch {
        setLines([]);
      }
    });
  }, []);

  useEffect(() => {
    void AsyncStorage.setItem(key, JSON.stringify(lines));
  }, [lines]);

  const store = useMemo<CartStore>(() => ({
    lines,
    add: (dishId, dishName) => {
      setLines((current) => {
        const existing = current.find((line) => line.dishId === dishId);
        if (!existing) return [...current, { dishId, dishName, portions: 1 }];
        return current.map((line) => line.dishId === dishId ? { ...line, portions: line.portions + 1 } : line);
      });
    },
    change: (dishId, portions) => {
      setLines((current) => portions <= 0
        ? current.filter((line) => line.dishId !== dishId)
        : current.map((line) => line.dishId === dishId ? { ...line, portions } : line));
    },
    clear: () => setLines([]),
  }), [lines]);

  return <CartContext.Provider value={store}>{children}</CartContext.Provider>;
}

export function useCart() {
  const value = useContext(CartContext);
  if (!value) throw new Error("Корзина не инициализирована");
  return value;
}
