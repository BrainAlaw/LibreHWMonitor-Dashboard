using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PCMonitor.Server;
using PCMonitor.Settings;
using PCMonitor.Telemetry;
using Xunit;

namespace PCMonitor.Tests;

public sealed class DiagnosticsAccessTests
{
    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("::ffff:192.168.1.20")]
    [InlineData("8.8.8.8")]
    public async Task DiagnosticDownloadRejectsRemoteClientsEvenWithForwardedLoopbackHeader(string address)
    {
        await using WebApplication app = WebApplication.CreateBuilder().Build();
        SettingsStore settings = new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));
        using TelemetryCollector collector = new(settings, _ => new FakeSource(() => []), true);
        Api.Map(app, 8096, settings, collector, new SystemActions(_ => Task.FromResult(true), () => false, _ => Task.FromResult(true)));
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>().Single(e => e.RoutePattern.RawText == "/api/diagnostics");
        DefaultHttpContext context = new();
        context.RequestServices = app.Services;
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Content-Disposition"));
    }
}
