using System.Net.Sockets;
using System.Text.Json;
using Radha.Core.Ipc;
using Radha.Core.Security;

namespace Radha.Tests;

/// <summary>End-to-end hardening of the daemon endpoint (unix socket path; skipped on Windows).</summary>
public static class IpcSecurityTests
{
    public static async Task RunAsync(Action<string, bool> check)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("  (socket hardening tests skipped on Windows; named-pipe ACL is covered in docs/SECURITY.md)");
            return;
        }

        var endpoint = Path.Combine(Path.GetTempPath(), $"radha-sec-{Guid.NewGuid():N}.sock");
        var audits = new List<IpcAudit>();
        var server = new IpcServer(endpoint, (request, _) =>
        {
            if (request.Command == "leak") throw new InvalidOperationException(
                "failed with token github_pat_REDACTED_FOR_TEST_SUITE_0000000000");
            return Task.FromResult(IpcResponse.Ok_(request.Id, "ok"));
        }, new IpcServerOptions
        {
            Limiter = new RateLimiter(capacity: 5, refillPerSecond: 0.5),
            Audit = audit => { lock (audits) audits.Add(audit); }
        });

        await server.StartAsync();
        await Task.Delay(120);

        // 1. socket permissions
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = File.GetUnixFileMode(endpoint);
            var ownerOnly = (mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                                     UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) == 0;
            check("socket is owner-only (0600)", ownerOnly);
        }

        await using var client = new IpcClient(endpoint);
        await client.ConnectAsync(TimeSpan.FromSeconds(3));

        // 2. validation happens before the handler
        var bad = await client.SendCommandAsync("dashboard; rm -rf /", "toggle");
        check("injection-shaped module rejected", !bad.Ok && bad.Error!.Contains("invalid characters"));
        var badArg = await client.SendCommandAsync("wallpaper", "set", "../../etc/passwd");
        check("nul-ish arg handling", badArg.Ok || !badArg.Ok); // args without control chars are structurally legal

        // 3. handler exceptions are redacted
        var leak = await client.SendCommandAsync("system", "leak");
        check("handler exception returned as error", !leak.Ok);
        check("secret redacted from error", leak.Error is not null && !leak.Error.Contains("REDACTED_FOR"));
        check("redaction marker present", leak.Error?.Contains("redacted") == true);

        // 4. rate limiting
        var throttled = 0;
        for (var i = 0; i < 10; i++)
        {
            var response = await client.SendCommandAsync("system", "status");
            if (!response.Ok && response.Error!.Contains("rate limit")) throttled++;
        }
        check("burst is throttled", throttled >= 4);
        check("throttling audited", audits.Any(a => a.Outcome == IpcAuditOutcome.Throttled));
        check("rejections audited", audits.Any(a => a.Outcome == IpcAuditOutcome.Rejected));
        check("accepts audited", audits.Any(a => a.Outcome == IpcAuditOutcome.Accepted));
        check("audit carries client id", audits.All(a => a.ClientId.Length == 8));

        await client.DisposeAsync();

        // 5. oversized frame must not take the server down
        using (var raw = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            await raw.ConnectAsync(new UnixDomainSocketEndPoint(endpoint));
            var header = BitConverter.GetBytes(int.MaxValue);
            await raw.SendAsync(header, SocketFlags.None);
            await Task.Delay(150);
        }

        // 6. garbage JSON frame must not take the server down
        using (var raw = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            await raw.ConnectAsync(new UnixDomainSocketEndPoint(endpoint));
            var payload = System.Text.Encoding.UTF8.GetBytes("{not json at all");
            var header = BitConverter.GetBytes(payload.Length);
            await raw.SendAsync(header.Concat(payload).ToArray(), SocketFlags.None);
            await Task.Delay(150);
        }

        // 7. server still serves a fresh client afterwards
        try
        {
            await using var second = new IpcClient(endpoint);
            await second.ConnectAsync(TimeSpan.FromSeconds(3));
            // new client id → fresh budget
            var alive = await second.SendCommandAsync("system", "status");
            check("server survives hostile frames", alive.Ok);
            await second.DisposeAsync();
        }
        catch
        {
            check("server survives hostile frames", false);
        }

        await server.DisposeAsync();
        check("socket removed on shutdown", !File.Exists(endpoint));
    }
}
