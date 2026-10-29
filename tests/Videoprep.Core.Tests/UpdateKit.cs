using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Videoprep.Core.Update;

namespace Videoprep.Core.Tests;

/// <summary>An in-memory GitHub: routes by address, redirects, and a record of what was asked. No network is ever used.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string Api = "https://api.github.com/repos/nicolasrabe/videoprep-next/releases?per_page=30";
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Asked { get; } = [];

    public static string Asset(string tag, string name) => $"https://github.com/nicolasrabe/videoprep-next/releases/download/{tag}/{name}";

    public void Bytes(string url, byte[] data, string type = "application/octet-stream") =>
        Routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) { Headers = { { "Content-Type", type } } } };

    public void Text(string url, string text) => Bytes(url, Encoding.UTF8.GetBytes(text), "text/plain");

    public void Redirect(string from, string to, int code = 302) =>
        Routes[from] = () => new HttpResponseMessage((HttpStatusCode)code) { Headers = { Location = new Uri(to, UriKind.RelativeOrAbsolute) } };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Asked.Add(request.RequestUri!.ToString());
        return Task.FromResult(Routes.TryGetValue(request.RequestUri.ToString(), out var make) ? make() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public UpdateClient Client() => new(this, "videoprep-tests/1");
}

internal static class UpdateKit
{
    /// <summary>Something that passes for a Windows x64 program: MZ, a PE header for AMD64 and enough bytes.</summary>
    public static byte[] FakeExe(string marker = "a")
    {
        var b = new byte[300_000];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(b, 0x3C);
        b[0x80] = (byte)'P'; b[0x81] = (byte)'E';
        BitConverter.GetBytes(ExeInspector.MachineAmd64).CopyTo(b, 0x84);
        Encoding.ASCII.GetBytes(marker).CopyTo(b, 1000);
        new Random(marker.GetHashCode()).NextBytes(b.AsSpan(2000, 1000));
        return b;
    }

    public static byte[] Zip(params (string Name, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, data) in files)
            {
                using var s = z.CreateEntry(name, CompressionLevel.Fastest).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    public static string Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string ReleaseJson(string tag, bool prerelease = false, bool draft = false, bool withZip = true, bool withSums = true, string rid = "win-x64") =>
        $$"""
        [{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},
          "html_url":"https://github.com/nicolasrabe/videoprep-next/releases/tag/{{tag}}","body":"notes",
          "assets":[{{(withZip ? $$"""{"name":"videoprep-{{tag}}-{{rid}}.zip","size":123,"browser_download_url":"https://evil.example/x.zip"}""" : "")}}{{(withZip && withSums ? "," : "")}}{{(withSums ? """{"name":"SHA256SUMS.txt","size":95}""" : "")}}]}]
        """;

    /// <summary>A whole fake release: list, sums and zip under their GitHub addresses.</summary>
    public static (FakeGitHub Hub, ReleaseInfo Release, byte[] Exe) Publish(string tag = "v0.3.0", Func<byte[], byte[]>? tamperZip = null, string? sumsOverride = null)
    {
        var hub = new FakeGitHub();
        var exe = FakeExe(tag);
        var zip = Zip(("videoprep.exe", exe));
        var zipName = $"videoprep-{tag}-win-x64.zip";
        hub.Text(FakeGitHub.Api, ReleaseJson(tag));
        hub.Text(FakeGitHub.Asset(tag, "SHA256SUMS.txt"), sumsOverride ?? $"{Hex(zip)}  {zipName}\n");
        hub.Bytes(FakeGitHub.Asset(tag, zipName), tamperZip?.Invoke(zip) ?? zip);
        var release = ReleaseFeed.PickNewer(ReleaseJson(tag), SemVer.Parse("0.2.0"), "win-x64")!;
        return (hub, release, exe);
    }
}
