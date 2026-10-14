import type { WeeklyForecastReport } from "@repo/contracts";
import { Empty, Panel, Qty } from "../ui";
import { forecastBasis, PurchaseButton } from "./PurchaseQr";

export function WeeklyForecastPanel({
  report,
  shortagesOnly,
  plain,
}: {
  report: WeeklyForecastReport;
  shortagesOnly?: boolean;
  plain?: boolean;
}) {
  const items = shortagesOnly ? report.items.filter((item) => item.shortfall > 0) : report.items;
  const hasConsumption = report.items.some((item) => item.consumedThisWeek > 0);
  const body = (
    <>
      {plain ? null : <p className="reason">
        Расход с понедельника ({report.elapsedDays} дн. этой недели) переносится на следующие 7 дней.
        Партия, которая истечёт раньше, чем её успеют списать, в покрытие не входит.
        {" "}{forecastBasis(report.ordersThisWeek)}
      </p>}
      {!plain && items.some((item) => item.shortfall > 0) ? (
        <PurchaseButton label="В заказ всё, чего не хватит" />
      ) : null}
      {items.length === 0 ? (
        <Empty>
          {hasConsumption
            ? "По темпу этой недели склад закрывает следующую."
            : "За эту неделю заказов ещё не было, прогноз расхода пустой."}
        </Empty>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Продукт</th>
              <th>За эту неделю</th>
              <th>Нужно на следующую</th>
              <th>На складе</th>
              <th>Не хватит</th>
              <th>Блюда</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.productId}>
                <td>{item.productName}</td>
                <td><Qty value={item.consumedThisWeek} unit={item.unit} /></td>
                <td><Qty value={item.projectedNeed} unit={item.unit} /></td>
                <td><Qty value={item.onHand} unit={item.unit} /></td>
                <td className={item.shortfall > 0 ? "error-text" : undefined}>
                  {item.shortfall > 0 ? <Qty value={item.shortfall} unit={item.unit} /> : "хватит"}
                </td>
                <td className="reason">{item.dishes.join(", ") || "—"}</td>
                <td>{item.shortfall > 0 ? <PurchaseButton productIds={[item.productId]} /> : null}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
  if (plain) return body;
  return (
    <Panel title={shortagesOnly ? "На следующей неделе не хватит" : "Прогноз на неделю"}>
      {body}
    </Panel>
  );
}
