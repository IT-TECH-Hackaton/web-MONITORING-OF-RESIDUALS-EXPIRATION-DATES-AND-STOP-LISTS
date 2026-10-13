namespace Monitoring.Core.Inventory;

public sealed record QualityCertificate(string Number, string Issuer, DateTime IssuedAt, DateTime ExpiresAt);

public sealed record CertificateVerdict(bool Ok, string? Reason);

/// <summary>Проверка сертификата на QR поставки. Не проходит — всю поставку отклоняем, склад не трогаем.</summary>
public static class CertificateCheck
{
    /// <summary>Вход: сертификат и сейчас. Выход: годен или текст, чего не хватает (номер, орган, срок).</summary>
    public static CertificateVerdict Evaluate(QualityCertificate? certificate, DateTime utcNow)
    {
        if (certificate is null || string.IsNullOrWhiteSpace(certificate.Number))
        {
            return new CertificateVerdict(false, "Нет номера сертификата качества.");
        }

        if (string.IsNullOrWhiteSpace(certificate.Issuer))
        {
            return new CertificateVerdict(false, "Не указан орган, выдавший сертификат.");
        }

        if (certificate.IssuedAt > utcNow)
        {
            return new CertificateVerdict(false, "Дата выдачи сертификата ещё не наступила.");
        }

        if (certificate.ExpiresAt <= certificate.IssuedAt)
        {
            return new CertificateVerdict(false, "Срок сертификата раньше даты выдачи.");
        }

        if (certificate.ExpiresAt <= utcNow)
        {
            return new CertificateVerdict(false, "Сертификат качества просрочен.");
        }

        return new CertificateVerdict(true, null);
    }
}
