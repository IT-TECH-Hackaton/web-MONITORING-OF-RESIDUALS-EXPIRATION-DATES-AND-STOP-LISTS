using Monitoring.Core.Inventory;

namespace Monitoring.Core.Tests;

public class CertificateCheckTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Valid_certificate_passes()
    {
        var verdict = CertificateCheck.Evaluate(
            new QualityCertificate("ВСД-88421", "Россельхознадзор", Now.AddDays(-1), Now.AddDays(10)),
            Now);

        Assert.True(verdict.Ok);
    }

    [Fact]
    public void Missing_certificate_fails()
    {
        var verdict = CertificateCheck.Evaluate(null, Now);

        Assert.False(verdict.Ok);
        Assert.Contains("сертификата", verdict.Reason);
    }

    [Fact]
    public void Expired_certificate_fails()
    {
        var verdict = CertificateCheck.Evaluate(
            new QualityCertificate("ВСД-00000", "Россельхознадзор", Now.AddDays(-10), Now.AddDays(-1)),
            Now);

        Assert.False(verdict.Ok);
        Assert.Contains("просрочен", verdict.Reason);
    }
}
