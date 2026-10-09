using System.Text.Json;
using Monitoring.Core;
using Monitoring.Core.Inventory;
using Monitoring.Core.Time;

namespace Monitoring.Infrastructure.Services;

public sealed class QrIntakeService(IClock clock, DeliveryService deliveries, StockReadModel stock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Sample(bool valid)
    {
        var now = clock.UtcNow;
        var document = new DeliveryQrDocument(
            Guid.NewGuid(),
            valid ? $"QR-{Random.Shared.Next(1000, 9999)}" : $"QR-BAD-{Random.Shared.Next(1000, 9999)}",
            valid ? "Северная рыба" : "Сомнительный склад",
            [
                new DeliveryQrItem(
                    Guid.NewGuid(),
                    valid ? "Треска" : "Креветки",
                    "G",
                    1200,
                    72,
                    null,
                    new QualityCertificate(
                        valid ? "ВСД-88421" : "ВСД-00000",
                        "Россельхознадзор",
                        now.AddDays(-2),
                        valid ? now.AddDays(14) : now.AddDays(-1))),
            ]);
        return JsonSerializer.Serialize(document, Json);
    }

    public async Task<ServiceResult<PurchaseDraft>> PurchaseRequestAsync(
        IReadOnlyList<Guid>? productIds,
        CancellationToken cancellationToken)
    {
        var report = await stock.WeeklyForecastAsync(cancellationToken);
        var selected = report.Items.Where(item => item.Shortfall > 0).ToList();
        if (productIds is { Count: > 0 })
        {
            var wanted = productIds.ToHashSet();
            selected = selected.Where(item => wanted.Contains(item.ProductId)).ToList();
        }

        if (selected.Count == 0)
        {
            return ServiceResult<PurchaseDraft>.Bad("Нечего заказывать: по прогнозу недели дефицита нет.");
        }

        var shelfLife = await stock.TypicalShelfLifeHoursAsync(selected.Select(item => item.ProductId).ToList(), cancellationToken);
        var number = $"ЗАК-{Random.Shared.Next(1000, 9999)}";
        var payload = PurchasePayload(
            number,
            selected.Select(item => new PurchaseQrLine(
                item.ProductId,
                item.ProductName,
                item.Unit.ToString(),
                item.Shortfall,
                shelfLife.GetValueOrDefault(item.ProductId, 24 * 7))).ToList());
        return ServiceResult<PurchaseDraft>.Ok(new PurchaseDraft(payload, number, selected.Count));
    }

    public string PurchasePayload(string number, IReadOnlyList<PurchaseQrLine> lines)
    {
        var document = new DeliveryQrDocument(
            Guid.NewGuid(),
            number,
            "Заявка на закупку",
            lines.Select(line => new DeliveryQrItem(
                line.ProductId,
                line.Name,
                line.Unit,
                line.Quantity,
                line.ShelfLifeHours,
                null,
                null)).ToList());
        return JsonSerializer.Serialize(document, Json);
    }

    public async Task<ServiceResult<QrIntakeResult>> ScanAsync(string? payload, CancellationToken cancellationToken)
    {
        DeliveryQrDocument? document;
        try
        {
            document = string.IsNullOrWhiteSpace(payload)
                ? null
                : JsonSerializer.Deserialize<DeliveryQrDocument>(payload, Json);
        }
        catch (JsonException)
        {
            document = null;
        }

        if (document is null)
        {
            return ServiceResult<QrIntakeResult>.Bad("QR не похож на накладную поставки.");
        }

        if (document.Id == Guid.Empty || string.IsNullOrWhiteSpace(document.Number) || document.Items.Count == 0)
        {
            return ServiceResult<QrIntakeResult>.Bad("В QR нет номера поставки или списка продуктов.");
        }

        var now = clock.UtcNow;
        var verdicts = document.Items.Select(item =>
        {
            var verdict = CertificateCheck.Evaluate(item.Certificate, now);
            return new QrLineVerdict(item.Name, item.Certificate?.Number, verdict.Ok, verdict.Reason);
        }).ToList();

        if (verdicts.Any(line => !line.Ok))
        {
            return ServiceResult<QrIntakeResult>.Ok(new QrIntakeResult(
                false,
                "Поставка не принята: сертификат качества не прошёл сверку. Склад не изменён.",
                verdicts,
                null));
        }

        var lines = new List<DeliveryLineCommand>();
        foreach (var item in document.Items)
        {
            if (!Enum.TryParse<Unit>(item.Unit, true, out var unit))
            {
                return ServiceResult<QrIntakeResult>.Bad("Единица измерения в QR: G, Ml или Pcs.");
            }

            lines.Add(new DeliveryLineCommand(
                item.ProductId,
                item.Name,
                unit,
                item.Quantity,
                item.ExpiresAt,
                item.ShelfLifeHours,
                new QualityCertificateCommand(
                    item.Certificate!.Number,
                    item.Certificate.Issuer,
                    item.Certificate.IssuedAt,
                    item.Certificate.ExpiresAt)));
        }

        var receipt = await deliveries.ReceiveAsync(
            new ReceiveDeliveryCommand(document.Id, document.Number, now, lines, document.Supplier),
            cancellationToken);
        if (receipt.Error is not null || receipt.Value is null)
        {
            return ServiceResult<QrIntakeResult>.Bad(receipt.Error ?? "Поставку не удалось записать.");
        }

        var summary = receipt.Value.Created
            ? $"Поставка {document.Number} принята. Сертификаты сверены, партии на складе."
            : $"Поставка {document.Number} уже была принята раньше. Повторное сканирование склад не меняет.";
        return ServiceResult<QrIntakeResult>.Ok(new QrIntakeResult(true, summary, verdicts, receipt.Value.Delivery));
    }

    private sealed record DeliveryQrDocument(
        Guid Id,
        string Number,
        string? Supplier,
        IReadOnlyList<DeliveryQrItem> Items);

    private sealed record DeliveryQrItem(
        Guid ProductId,
        string Name,
        string Unit,
        decimal Quantity,
        double? ShelfLifeHours,
        DateTime? ExpiresAt,
        QualityCertificate? Certificate);
}
