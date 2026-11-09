using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ProfetAPI.Services;
using Xunit;

namespace ProfetAPI.Tests;

public class WebhookSecurityTests
{
    private static IConfiguration Cfg(string? secret) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            secret == null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["TwoChat:WebhookSecret"] = secret }).Build();

    private static HttpRequest Req(string? querySecret = null, string? headerSecret = null)
    {
        var ctx = new DefaultHttpContext();
        if (querySecret != null) ctx.Request.QueryString = new QueryString("?secret=" + Uri.EscapeDataString(querySecret));
        if (headerSecret != null) ctx.Request.Headers["X-Webhook-Secret"] = headerSecret;
        return ctx.Request;
    }

    [Fact] public void SinSecretoConfigurado_PermiteCompatibilidad() =>
        Assert.True(WebhookSecurity.IsTwoChatWebhookAuthorized(Cfg(null), Req()));

    [Fact] public void ConSecreto_SinMandarlo_Rechaza() =>
        Assert.False(WebhookSecurity.IsTwoChatWebhookAuthorized(Cfg("abc123"), Req()));

    [Fact] public void ConSecreto_Incorrecto_Rechaza() =>
        Assert.False(WebhookSecurity.IsTwoChatWebhookAuthorized(Cfg("abc123"), Req("otro")));

    [Fact] public void ConSecreto_EnQuery_Acepta() =>
        Assert.True(WebhookSecurity.IsTwoChatWebhookAuthorized(Cfg("abc123"), Req("abc123")));

    [Fact] public void ConSecreto_EnHeader_Acepta() =>
        Assert.True(WebhookSecurity.IsTwoChatWebhookAuthorized(Cfg("abc123"), Req(null, "abc123")));

    [Theory]
    [InlineData(null, "x")]
    [InlineData("x", null)]
    [InlineData("", "")]
    [InlineData("a", "b")]
    public void SecretsMatch_CasosNegativos(string? expected, string? provided) =>
        Assert.False(WebhookSecurity.SecretsMatch(expected, provided));
}
