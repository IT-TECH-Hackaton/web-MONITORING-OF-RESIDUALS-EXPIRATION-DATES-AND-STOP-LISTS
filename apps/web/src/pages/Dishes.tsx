import { newId, type Unit } from "@repo/contracts";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useSession } from "../session";
import { Empty, ErrorText, Panel } from "../ui";

type Line = { key: string; productId: string; quantity: string };

export function DishesPage() {
  const { client } = useSession();
  const queryClient = useQueryClient();
  const dishes = useQuery({ queryKey: ["dishes"], queryFn: () => client.dishes() });
  const products = useQuery({ queryKey: ["products"], queryFn: () => client.products() });
  const [editing, setEditing] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [productName, setProductName] = useState("");
  const [unit, setUnit] = useState<Unit>("G");
  const [lines, setLines] = useState<Line[]>([{ key: newId(), productId: "", quantity: "" }]);
  const [error, setError] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: () => client.saveDish({
      id: editing ?? undefined,
      name,
      ingredients: lines.filter((line) => line.productId).map((line) => ({ productId: line.productId, quantity: Number(line.quantity) })),
    }),
    onSuccess: async () => {
      setEditing(null);
      setName("");
      setLines([{ key: newId(), productId: "", quantity: "" }]);
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ["dishes"] });
    },
    onError: (caught) => setError(caught instanceof Error ? caught.message : "Рецепт не сохранён"),
  });

  const createProduct = useMutation({
    mutationFn: () => client.createProduct(productName, unit),
    onSuccess: async () => {
      setProductName("");
      await queryClient.invalidateQueries({ queryKey: ["products"] });
    },
  });

  return (
    <>
      <div className="page-title">
        <div>
          <h1>Блюда</h1>
          <p>Норма ингредиента задаётся на одну порцию.</p>
        </div>
      </div>
      <div className="split">
        <Panel title="Меню">
          {dishes.isLoading ? <p>Загрузка…</p> : null}
          {dishes.error ? <ErrorText error={dishes.error} /> : null}
          {dishes.data?.length === 0 ? <Empty>Блюд пока нет.</Empty> : null}
          <div className="stack">
            {dishes.data?.map((dish) => (
              <article key={dish.id}>
                <strong>{dish.name}</strong>
                <p className="reason">{dish.ingredients.map((line) => `${line.productName} ${line.quantity} ${line.unit === "G" ? "г" : line.unit === "Ml" ? "мл" : "шт"}`).join(", ")}</p>
                <button className="btn-ghost" onClick={() => {
                  setEditing(dish.id);
                  setName(dish.name);
                  setLines(dish.ingredients.map((line) => ({ key: line.productId, productId: line.productId, quantity: String(line.quantity) })));
                }}>Изменить</button>
              </article>
            ))}
          </div>
        </Panel>
        <div className="stack">
          <Panel title={editing ? "Рецепт" : "Новое блюдо"}>
            <form className="form" onSubmit={(event) => { event.preventDefault(); save.mutate(); }}>
              <label>Название<input value={name} onChange={(event) => setName(event.target.value)} required /></label>
              {lines.map((line) => (
                <div className="line" key={line.key}>
                  <label>
                    Продукт
                    <select value={line.productId} onChange={(event) => setLines((current) => current.map((item) => item.key === line.key ? { ...item, productId: event.target.value } : item))} required>
                      <option value="">Выберите</option>
                      {products.data?.map((product) => <option key={product.id} value={product.id}>{product.name}</option>)}
                    </select>
                  </label>
                  <label>На порцию<input type="number" min="0.001" step="0.001" value={line.quantity} onChange={(event) => setLines((current) => current.map((item) => item.key === line.key ? { ...item, quantity: event.target.value } : item))} required /></label>
                </div>
              ))}
              {error ? <p className="error-text">{error}</p> : null}
              <div className="btn-row">
                <button className="btn" disabled={save.isPending}>Сохранить</button>
                <button className="btn-ghost" type="button" onClick={() => setLines((current) => [...current, { key: newId(), productId: "", quantity: "" }])}>Ингредиент</button>
              </div>
            </form>
          </Panel>
          <Panel title="Новый продукт">
            <form className="form" onSubmit={(event) => { event.preventDefault(); createProduct.mutate(); }}>
              <label>Название<input value={productName} onChange={(event) => setProductName(event.target.value)} required /></label>
              <label>
                Единица
                <select value={unit} onChange={(event) => setUnit(event.target.value as Unit)}>
                  <option value="G">г</option>
                  <option value="Ml">мл</option>
                  <option value="Pcs">шт</option>
                </select>
              </label>
              <button className="btn-ghost">Добавить в каталог</button>
            </form>
            {products.data ? <p className="hint">В каталоге {products.data.length}. На складе количество появится после поставки.</p> : null}
          </Panel>
        </div>
      </div>
    </>
  );
}
