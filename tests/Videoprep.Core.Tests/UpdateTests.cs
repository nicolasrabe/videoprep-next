using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Videoprep.Core.Update;

namespace Videoprep.Core.Tests;

public sealed class UpdateVersionAndAddressTests
{
    [Theory]
    [InlineData("0.2.0", "0.3.0")]
    [InlineData("0.3.0-rc.1", "0.3.0")]
    [InlineData("0.3.0-rc.1", "0.3.0-rc.2")]
    [InlineData("0.3.0-rc.2", "0.3.0-rc.10")]
    [InlineData("0.3.0-alpha", "0.3.0-beta")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("0.99.0", "1.0.0")]
    public void Versions_are_ordered_like_semver(string older, string newer)
    {
        Assert.True(SemVer.Parse(newer).IsNewerThan(SemVer.Parse(older)));
        Assert.False(SemVer.Parse(older).IsNewerThan(SemVer.Parse(newer)));
    }

    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("0.3.0+abc123", "0.3.0")]
    [InlineData("V1.2.3-rc.1+meta", "1.2.3-rc.1")]
    public void Versions_are_read_with_a_v_and_build_metadata_ignored(string text, string normal) =>
        Assert.Equal(normal, SemVer.Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("0.3")]
    [InlineData("0.3.0.1")]
    [InlineData("a.b.c")]
    [InlineData("-1.0.0")]
    [InlineData("0.3.0-")]
    [InlineData("0.3.0-a..b")]
    [InlineData("0.3.0-ñ")]
    [InlineData("0.3.0; rm -rf")]
    public void Strange_versions_are_refused(string text) => Assert.False(SemVer.TryParse(text, out _));

    [Fact]
    public void The_same_version_is_not_newer() => Assert.False(SemVer.Parse("0.3.0").IsNewerThan(SemVer.Parse("0.3.0")));

    [Theory]
    [InlineData("https://github.com/nicolasrabe/videoprep-next/releases", true)]
    [InlineData("https://api.github.com/repos/x/y/releases", true)]
    [InlineData("https://objects.githubusercontent.com/abc", true)]
    [InlineData("https://release-assets.githubusercontent.com/abc?x=1", true)]
    [InlineData("http://github.com/x", false)]                                  // not https
    [InlineData("https://github.com.evil.example/x", false)]                    // look-alike
    [InlineData("https://evilgithub.com/x", false)]
    [InlineData("https://githubusercontent.com/x", false)]                      // the bare domain is not one of GitHub's asset hosts
    [InlineData("https://evilgithubusercontent.com/x", false)]
    [InlineData("https://user:pw@github.com/x", false)]                         // address with a user name
    [InlineData("https://github.com:8443/x", false)]                            // other port
    [InlineData("http://127.0.0.1:8080/releases", false)]                       // the local test server is not allowed in this check without the debug variable
    [InlineData("file:///C:/x.zip", false)]
    [InlineData("ftp://github.com/x", false)]
    public void Only_github_hosts_over_https_are_allowed(string url, bool allowed) =>
        Assert.Equal(allowed, UpdateEndpoint.IsAllowed(new Uri(url)));

    [Fact]
    public void The_download_addresses_are_built_from_the_tag()
    {
        Assert.Equal("https://github.com/nicolasrabe/videoprep-next/releases/download/v0.3.0/SHA256SUMS.txt",
                     UpdateEndpoint.AssetUrl("v0.3.0", "SHA256SUMS.txt").ToString());
        Assert.True(UpdateEndpoint.IsAllowed(UpdateEndpoint.AssetUrl("v0.3.0-rc.1", "videoprep-v0.3.0-rc.1-win-x64.zip")));
    }

    [Fact]
    public void The_text_of_the_development_server_variable_is_only_in_debug_builds()
    {
        // CI builds and tests in Release: this proves a release build cannot be pointed at another server by an environment variable.
        var bytes = File.ReadAllBytes(typeof(UpdateEndpoint).Assembly.Location);
        const string name = "VIDEOPREP_UPDATE_URL";
        var inside = IndexOf(bytes, Encoding.ASCII.GetBytes(name)) >= 0 || IndexOf(bytes, Encoding.Unicode.GetBytes(name)) >= 0;
#if DEBUG
        Assert.True(inside);
#else
        Assert.False(inside, "the development-only variable is inside a Release Videoprep.Core.dll");
#endif
    }

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);
}

public sealed class ReleaseFeedTests
{
    private static ReleaseInfo? Pick(string json, string current = "0.2.0", bool? pre = null) =>
        ReleaseFeed.PickNewer(json, SemVer.Parse(current), "win-x64", pre);

    [Fact]
    public void A_newer_release_with_its_files_is_found()
    {
        var r = Pick(UpdateKit.ReleaseJson("v0.3.0", prerelease: true))!;
        Assert.Equal("0.3.0", r.Version.ToString());
        Assert.True(r.Prerelease);
        Assert.Equal("videoprep-v0.3.0-win-x64.zip", r.Zip!.Name);
        // The address comes from the tag, never from the list (the list said evil.example).
        Assert.Equal(FakeGitHub.Asset("v0.3.0", "videoprep-v0.3.0-win-x64.zip"), r.Zip.Url.ToString());
        Assert.Equal(FakeGitHub.Asset("v0.3.0", "SHA256SUMS.txt"), r.Sums!.Url.ToString());
    }

    [Theory]
    [InlineData("0.3.0")]   // the same
    [InlineData("0.4.0")]   // an older one is not an update
    public void The_same_or_an_older_release_is_not_offered(string installed) =>
        Assert.Null(Pick(UpdateKit.ReleaseJson("v0.3.0", prerelease: true), installed));

    [Fact]
    public void A_draft_is_never_offered() => Assert.Null(Pick(UpdateKit.ReleaseJson("v0.3.0", prerelease: true, draft: true)));

    [Fact]
    public void Prereleases_are_offered_while_the_installed_version_is_0x_and_not_from_1x()
    {
        var json = UpdateKit.ReleaseJson("v1.1.0", prerelease: true);
        Assert.NotNull(Pick(json, "0.9.0"));
        Assert.Null(Pick(json, "1.0.0"));
        Assert.NotNull(Pick(json, "1.0.0", pre: true));
        Assert.Null(Pick(json, "0.9.0", pre: false));
    }

    [Fact]
    public void A_release_without_a_zip_for_this_system_or_without_sums_is_still_reported_but_cannot_be_installed()
    {
        var noZip = Pick(UpdateKit.ReleaseJson("v0.3.0", prerelease: true, withZip: false))!;
        Assert.Null(noZip.Zip);
        var otherSystem = Pick(UpdateKit.ReleaseJson("v0.3.0", prerelease: true, rid: "osx-arm64"))!;
        Assert.Null(otherSystem.Zip);
        var noSums = Pick(UpdateKit.ReleaseJson("v0.3.0", prerelease: true, withSums: false))!;
        Assert.Null(noSums.Sums);
    }

    [Fact]
    public void The_newest_of_several_wins_and_strange_entries_are_skipped()
    {
        const string json = """
            [{"tag_name":"v0.2.5","prerelease":true,"assets":[]},{"tag_name":"nightly","prerelease":true},
             {"tag_name":"v0.4.0-rc.1","prerelease":true,"assets":[]},{"tag_name":"v0.3.0","prerelease":true,"assets":[]},
             42,"text",{"tag_name":7}]
            """;
        Assert.Equal("0.4.0-rc.1", Pick(json)!.Version.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{}]")]
    public void Empty_answers_mean_no_update(string json) => Assert.Null(Pick(json));

    [Fact]
    public void A_page_address_outside_github_is_replaced_by_the_releases_page()
    {
        var json = UpdateKit.ReleaseJson("v0.3.0", prerelease: true).Replace("https://github.com/nicolasrabe/videoprep-next/releases/tag/v0.3.0", "https://evil.example/page");
        Assert.Equal(UpdateEndpoint.ReleasesPage, Pick(json)!.PageUrl);
    }

    [Fact]
    public void The_check_is_due_once_a_day()
    {
        var now = DateTimeOffset.Parse("2026-10-29T12:00:00-03:00");
        Assert.True(UpdateChecker.IsDue("", now));
        Assert.True(UpdateChecker.IsDue("garbage", now));
        Assert.False(UpdateChecker.IsDue(now.AddHours(-3).ToString("o"), now));
        Assert.False(UpdateChecker.IsDue(now.AddHours(-23).ToString("o"), now));
        Assert.True(UpdateChecker.IsDue(now.AddHours(-25).ToString("o"), now));
        Assert.True(UpdateChecker.IsDue(now.AddHours(5).ToString("o"), now));   // a date in the future (clock changed): look again
    }
}

public sealed class UpdateClientTests
{
    private static Uri Api => new(FakeGitHub.Api);

    [Fact]
    public async Task A_redirect_inside_github_is_followed()
    {
        var hub = new FakeGitHub();
        hub.Redirect(FakeGitHub.Api, "https://objects.githubusercontent.com/list");
        hub.Text("https://objects.githubusercontent.com/list", "hello");
        Assert.Equal("hello", await hub.Client().GetStringAsync(Api, 1000, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://evil.example/x")]                       // another host
    [InlineData("http://objects.githubusercontent.com/x")]       // https downgraded to http
    [InlineData("https://github.com.evil.example/x")]
    [InlineData("file:///C:/Windows/win.ini")]
    public async Task A_redirect_to_an_address_that_is_not_allowed_is_refused_and_never_visited(string to)
    {
        var hub = new FakeGitHub();
        hub.Redirect(FakeGitHub.Api, to);
        hub.Text(to, "secret");
        var ex = await Assert.ThrowsAsync<UpdateException>(() => hub.Client().GetStringAsync(Api, 1000, TestContext.Current.CancellationToken));
        Assert.Equal(UpdateFailure.UnsafeAddress, ex.Failure);
        Assert.DoesNotContain(to, hub.Asked);
    }

    [Fact]
    public async Task A_loop_of_redirects_stops_after_five_hops()
    {
        var hub = new FakeGitHub();
        hub.Redirect(FakeGitHub.Api, "https://github.com/a");
        hub.Redirect("https://github.com/a", FakeGitHub.Api);
        var ex = await Assert.ThrowsAsync<UpdateException>(() => hub.Client().GetStringAsync(Api, 1000, TestContext.Current.CancellationToken));
        Assert.Equal(UpdateFailure.UnsafeAddress, ex.Failure);
        Assert.InRange(hub.Asked.Count, 1, UpdateClient.MaxHops + 1);
    }

    [Fact]
    public async Task A_relative_redirect_stays_on_the_same_host()
    {
        var hub = new FakeGitHub();
        hub.Redirect(FakeGitHub.Api, "/other");
        hub.Text("https://api.github.com/other", "ok");
        Assert.Equal("ok", await hub.Client().GetStringAsync(Api, 1000, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_download_larger_than_its_limit_is_cut_off()
    {
        var hub = new FakeGitHub();
        hub.Bytes(FakeGitHub.Api, new byte[5000]);
        var ex = await Assert.ThrowsAsync<UpdateException>(() => hub.Client().GetStringAsync(Api, 1000, TestContext.Current.CancellationToken));
        Assert.Equal(UpdateFailure.TooBig, ex.Failure);
    }

    [Fact]
    public async Task A_missing_page_is_a_network_problem_not_a_crash()
    {
        var ex = await Assert.ThrowsAsync<UpdateException>(() => new FakeGitHub().Client().GetStringAsync(Api, 1000, TestContext.Current.CancellationToken));
        Assert.Equal(UpdateFailure.Network, ex.Failure);
    }

    [Fact]
    public async Task The_checker_reads_the_list_and_a_broken_list_is_a_network_problem()
    {
        var (hub, _, _) = UpdateKit.Publish();
        var found = await new UpdateChecker(hub.Client()).CheckAsync(SemVer.Parse("0.2.0"), "win-x64", TestContext.Current.CancellationToken);
        Assert.Equal("0.3.0", found!.Version.ToString());
        Assert.Null(await new UpdateChecker(hub.Client()).CheckAsync(SemVer.Parse("0.3.0"), "win-x64", TestContext.Current.CancellationToken));

        hub.Text(FakeGitHub.Api, "<html>rate limited</html>");
        var ex = await Assert.ThrowsAsync<UpdateException>(() => new UpdateChecker(hub.Client()).CheckAsync(SemVer.Parse("0.2.0"), "win-x64", TestContext.Current.CancellationToken));
        Assert.Equal(UpdateFailure.Network, ex.Failure);
    }
}

public sealed class UpdateStagerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-upd-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private UpdateStager Stager(FakeGitHub hub, string version = "0.3.0") => new(hub.Client(), _dir, _ => version);

    private Task<StagedUpdate> Prepare(FakeGitHub hub, ReleaseInfo r, string version = "0.3.0") =>
        Stager(hub, version).PrepareAsync(r, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_good_release_is_downloaded_checked_and_unpacked()
    {
        var (hub, release, exe) = UpdateKit.Publish();
        var staged = await Prepare(hub, release);
        Assert.Equal(exe, await File.ReadAllBytesAsync(staged.ExePath, TestContext.Current.CancellationToken));
        Assert.Equal(UpdateKit.Hex(exe), staged.Sha256);
        Assert.Equal(["videoprep.exe"], Directory.GetFiles(Path.GetDirectoryName(staged.ExePath)!).Select(Path.GetFileName));   // the zip is gone
    }

    [Fact]
    public async Task A_zip_that_does_not_match_the_sums_is_not_installed_and_leaves_nothing_behind()
    {
        var (hub, release, _) = UpdateKit.Publish(tamperZip: z => { var c = (byte[])z.Clone(); c[^30] ^= 0xFF; return c; });
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Prepare(hub, release));
        Assert.Equal(UpdateFailure.ChecksumMismatch, ex.Failure);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task A_zip_swapped_for_another_one_is_caught_even_if_it_is_a_valid_zip()
    {
        var other = UpdateKit.Zip(("videoprep.exe", UpdateKit.FakeExe("evil")));
        var (hub, release, _) = UpdateKit.Publish(tamperZip: _ => other);
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Prepare(hub, release));
        Assert.Equal(UpdateFailure.ChecksumMismatch, ex.Failure);
    }

    [Theory]
    [InlineData("")]                                          // an empty file
    [InlineData("not a checksum file\n")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  other-file.zip\n")]   // lists something else
    public async Task Without_a_usable_checksum_nothing_is_installed(string sums)
    {
        var (hub, release, _) = UpdateKit.Publish(sumsOverride: sums);
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Prepare(hub, release));
        Assert.Equal(UpdateFailure.NoChecksum, ex.Failure);
        Assert.DoesNotContain(hub.Asked, a => a.EndsWith(".zip", StringComparison.Ordinal));   // not even downloaded
    }

    [Fact]
    public async Task A_release_without_sums_or_zip_is_refused_before_any_download()
    {
        var (hub, release, _) = UpdateKit.Publish();
        var ex1 = await Assert.ThrowsAsync<UpdateException>(() => Prepare(hub, release with { Sums = null }));
        Assert.Equal(UpdateFailure.NoChecksum, ex1.Failure);
        var ex2 = await Assert.ThrowsAsync<UpdateException>(() => Prepare(hub, release with { Zip = null }));
        Assert.Equal(UpdateFailure.NoInstaller, ex2.Failure);
        Assert.DoesNotContain(hub.Asked, a => a.Contains("download", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_that_is_not_a_windows_program_or_says_another_version_is_refused()
    {
        var notExe = new byte[300_000];
        Encoding.ASCII.GetBytes("#!/bin/sh").CopyTo(notExe, 0);
        foreach (var (data, version, failure) in new[]
                 {
                     (notExe, "0.3.0", UpdateFailure.BadExecutable),
                     (UpdateKit.FakeExe("x"), "0.2.9", UpdateFailure.WrongVersion),      // the program says another version than the release
                 })
        {
            var zip = UpdateKit.Zip(("videoprep.exe", data));
            var hub = new FakeGitHub();
            hub.Text(FakeGitHub.Asset("v0.3.0", "SHA256SUMS.txt"), $"{UpdateKit.Hex(zip)}  videoprep-v0.3.0-win-x64.zip\n");   // the sums are right: the contents are not
            hub.Bytes(FakeGitHub.Asset("v0.3.0", "videoprep-v0.3.0-win-x64.zip"), zip);
            var release = ReleaseFeed.PickNewer(UpdateKit.ReleaseJson("v0.3.0"), SemVer.Parse("0.2.0"), "win-x64")!;
            var ex = await Assert.ThrowsAsync<UpdateException>(() => Prepare(hub, release, version));
            Assert.Equal(failure, ex.Failure);
        }
    }

    [Fact]
    public async Task Old_update_folders_are_cleaned_but_only_inside_the_updates_folder()
    {
        var outside = Directory.CreateTempSubdirectory("videoprep-outside-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "x");
            var (hub, release, _) = UpdateKit.Publish();
            await Prepare(hub, release);
            Directory.CreateDirectory(Path.Combine(_dir, "0.2.5"));
            Stager(hub).CleanOld(release.Version);
            Assert.Equal(["0.3.0"], Directory.GetDirectories(_dir).Select(Path.GetFileName));
            Stager(hub).CleanOld();
            Assert.Empty(Directory.GetDirectories(_dir));
            Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
        }
        finally { Directory.Delete(outside, true); }
    }
}

public sealed class SafeZipTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-zip-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private string Make(params (string Name, byte[] Data)[] files)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, UpdateKit.Zip(files));
        return path;
    }

    private string Out => Path.Combine(_dir, "videoprep.exe");

    [Theory]
    [InlineData("../videoprep.exe")]             // zip-slip
    [InlineData("..\\..\\videoprep.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/System32/x.dll")]
    [InlineData("sub/videoprep.exe")]            // folders are not part of our zips
    [InlineData("videoprep.exe:stream")]         // alternate data stream
    public void Unsafe_names_refuse_the_whole_zip_and_write_nothing(string evil)
    {
        var zip = Make(("videoprep.exe", [1, 2, 3]), (evil, [9]));
        var ex = Assert.Throws<UpdateException>(() => SafeZip.ExtractOnly(zip, "videoprep.exe", Out, 1000));
        Assert.Equal(UpdateFailure.BadArchive, ex.Failure);
        Assert.False(File.Exists(Out));
    }

    [Fact]
    public void Other_flat_files_are_ignored_and_only_the_program_is_written()
    {
        var zip = Make(("videoprep.exe", [1, 2, 3]), ("README.txt", [4]));
        SafeZip.ExtractOnly(zip, "videoprep.exe", Out, 1000);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Out));
        Assert.Equal(["videoprep.exe"], Directory.GetFiles(_dir, "*", SearchOption.TopDirectoryOnly).Where(f => !f.EndsWith(".zip", StringComparison.Ordinal)).Select(Path.GetFileName));
    }

    [Fact]
    public void A_zip_without_the_program_or_with_it_twice_is_refused()
    {
        Assert.Equal(UpdateFailure.BadArchive, Assert.Throws<UpdateException>(() => SafeZip.ExtractOnly(Make(("other.exe", [1])), "videoprep.exe", Out, 1000)).Failure);
        Assert.Equal(UpdateFailure.BadArchive, Assert.Throws<UpdateException>(() => SafeZip.ExtractOnly(Make(("videoprep.exe", [1]), ("VIDEOPREP.EXE", [2])), "videoprep.exe", Out, 1000)).Failure);
    }

    [Fact]
    public void A_zip_that_unpacks_to_far_more_than_it_should_is_refused()
    {
        var zip = Make(("videoprep.exe", new byte[2_000_000]));          // compresses to almost nothing, like a zip bomb
        Assert.Equal(UpdateFailure.TooBig, Assert.Throws<UpdateException>(() => SafeZip.ExtractOnly(zip, "videoprep.exe", Out, 1_000_000)).Failure);
    }

    [Fact]
    public void Too_many_files_or_a_damaged_zip_are_refused()
    {
        var many = Make(Enumerable.Range(0, SafeZip.MaxEntries + 1).Select(i => ($"f{i}.txt", new byte[] { 1 })).ToArray());
        Assert.Equal(UpdateFailure.BadArchive, Assert.Throws<UpdateException>(() => SafeZip.ExtractOnly(many, "videoprep.exe", Out, 1000)).Failure);
        var junk = Path.Combine(_dir, "junk.zip");
        File.WriteAllText(junk, "this is not a zip");
        Assert.Equal(UpdateFailure.BadArchive, Assert.Throws<UpdateException>(() => SafeZip.ExtractOnly(junk, "videoprep.exe", Out, 1000)).Failure);
    }

    [Fact]
    public void Checksum_lines_are_read_in_both_sha256sum_styles_and_paths_are_ignored()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = Sha256Sums.Parse($"{a}  videoprep-v0.3.0-win-x64.zip\n{b} *other.zip\r\n{a}  ../escape.zip\nshort  x.zip\n");
        Assert.Equal(2, sums.Count);
        Assert.Equal(a, sums["videoprep-v0.3.0-win-x64.zip"]);
        Assert.Equal(b.ToLowerInvariant(), sums["other.zip"]);
    }
}

public sealed class UpdateApplierTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("videoprep-apply-").FullName;
    private readonly byte[] _oldExe = UpdateKit.FakeExe("old-0.2.9");
    private readonly byte[] _newExe = UpdateKit.FakeExe("new-0.3.0");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private string Target => Path.Combine(_dir, "install", "videoprep.exe");
    private string Staged => Path.Combine(_dir, "staged", "videoprep.exe");

    private ApplyRequest Setup(byte[]? staged = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Staged)!);
        File.WriteAllBytes(Target, _oldExe);
        File.WriteAllBytes(Staged, staged ?? _newExe);
        return new ApplyRequest(Target, Staged, UpdateKit.Hex(_newExe), 4242, "0.2.9", "0.3.0");
    }

    private sealed class FakeHost : IUpdateHost
    {
        public bool OldClosed { get; set; } = true;
        public int? ExitOfNew { get; set; }                       // null = keeps running
        public List<(string Exe, byte[] Content)> Launched { get; } = [];
        public List<string> Events { get; } = [];

        public Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct) => Task.FromResult(OldClosed);

        public IRunningApp Launch(string exe)
        {
            Events.Add("launch");
            Launched.Add((exe, File.ReadAllBytes(exe)));
            return new Running(Launched.Count == 1 ? ExitOfNew : null);
        }

        private sealed class Running(int? exit) : IRunningApp
        {
            public Task<int?> ExitCodeWithinAsync(TimeSpan time, CancellationToken ct) => Task.FromResult(exit);
        }
    }

    private static UpdateApplier Applier(FakeHost host, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(host, delay ?? ((_, _) => Task.CompletedTask), retryAttempts: 10, saveResult: r => host.Events.Add(r.Ok ? "save-ok" : "save-fail"));

    private void AssertInstalled(byte[] exe)
    {
        Assert.Equal(exe, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Target + ".new"));
    }

    [Fact]
    public async Task The_new_version_goes_in_the_old_one_stays_as_old_and_the_new_one_is_started()
    {
        var host = new FakeHost();
        var r = await Applier(host).RunAsync(Setup(), TestContext.Current.CancellationToken);
        Assert.True(r.Ok, r.Detail);
        AssertInstalled(_newExe);
        Assert.Equal(_oldExe, File.ReadAllBytes(Target + ".old"));              // the way back
        Assert.Equal([_newExe], host.Launched.Select(l => l.Content));
        Assert.Equal(["save-ok", "launch"], host.Events);
    }

    [Fact]
    public async Task A_previous_old_copy_is_replaced_by_the_newer_old_one()
    {
        var req = Setup();
        File.WriteAllText(Target + ".old", "from an update long ago");
        var r = await Applier(new FakeHost()).RunAsync(req, TestContext.Current.CancellationToken);
        Assert.True(r.Ok, r.Detail);
        Assert.Equal(_oldExe, File.ReadAllBytes(Target + ".old"));
    }

    [Fact]
    public async Task If_the_old_program_does_not_close_nothing_is_touched_and_nothing_is_started()
    {
        var host = new FakeHost { OldClosed = false };
        var r = await Applier(host).RunAsync(Setup(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateFailure.StillRunning, r.Failure);
        AssertInstalled(_oldExe);
        Assert.False(File.Exists(Target + ".old"));
        Assert.Empty(host.Launched);
    }

    [Fact]
    public async Task A_staged_file_that_is_not_the_checked_one_is_not_installed_and_the_old_program_is_opened_again()
    {
        var host = new FakeHost();
        var r = await Applier(host).RunAsync(Setup(staged: UpdateKit.FakeExe("tampered")), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateFailure.ChecksumMismatch, r.Failure);
        AssertInstalled(_oldExe);
        Assert.Equal([_oldExe], host.Launched.Select(l => l.Content));
    }

    [Fact]
    public async Task A_file_the_antivirus_holds_for_a_moment_is_retried_until_it_lets_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to move a file that another handle holds open");
        var req = Setup();
        FileStream? hold = new(Target, FileMode.Open, FileAccess.Read, FileShare.None);   // the installed program is busy
        var waits = 0;
        var r = await Applier(new FakeHost(), (_, _) =>
        {
            if (++waits == 3) { hold?.Dispose(); hold = null; }                              // released on the third retry
            return Task.CompletedTask;
        }).RunAsync(req, TestContext.Current.CancellationToken);
        hold?.Dispose();
        Assert.True(r.Ok, r.Detail);
        Assert.InRange(waits, 3, 4);
        AssertInstalled(_newExe);
        Assert.Equal(_oldExe, File.ReadAllBytes(Target + ".old"));
    }

    [Fact]
    public async Task A_file_that_stays_locked_gives_up_cleanly_and_the_old_version_keeps_working()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to move a file that another handle holds open");
        var req = Setup();
        using var hold = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.None);
        var waits = 0;
        var host = new FakeHost();
        var r = await Applier(host, (_, _) => { waits++; return Task.CompletedTask; }).RunAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateFailure.FilesLocked, r.Failure);
        Assert.Equal(10, waits);                                                              // it did try for a while
        hold.Dispose();
        AssertInstalled(_oldExe);                                                             // untouched, no leftovers
        Assert.False(File.Exists(Target + ".old"));
    }

    [Fact]
    public async Task A_new_version_that_closes_by_itself_is_rolled_back_and_the_old_one_is_opened()
    {
        var host = new FakeHost { ExitOfNew = -1073741819 };                                  // an access violation, like a crash at start
        var r = await Applier(host).RunAsync(Setup(), TestContext.Current.CancellationToken);
        Assert.Equal(UpdateFailure.NewVersionDidNotStart, r.Failure);
        AssertInstalled(_oldExe);
        Assert.Equal([_newExe, _oldExe], host.Launched.Select(l => l.Content));
        // Each result is on disk before the program it is meant for opens (that program tells the person).
        Assert.Equal(["save-ok", "launch", "save-fail", "launch"], host.Events);
    }

    [Fact]
    public async Task A_new_version_that_ends_normally_within_the_watch_time_is_kept()
    {
        var host = new FakeHost { ExitOfNew = 0 };                                            // the person closed it quickly: not a failure
        var r = await Applier(host).RunAsync(Setup(), TestContext.Current.CancellationToken);
        Assert.True(r.Ok, r.Detail);
        AssertInstalled(_newExe);
    }

    [Fact]
    public async Task The_first_ever_install_has_no_old_copy_to_keep()
    {
        var req = Setup();
        File.Delete(Target);
        var r = await Applier(new FakeHost()).RunAsync(req, TestContext.Current.CancellationToken);
        Assert.True(r.Ok, r.Detail);
        AssertInstalled(_newExe);
        Assert.False(File.Exists(Target + ".old"));
    }

    [Fact]
    public void The_command_line_survives_odd_paths_and_bad_ones_are_refused()
    {
        var odd = Path.Combine(Path.GetTempPath(), "Nico Pérez", "Mi Programa;&calc", "videoprep.exe");
        var req = new ApplyRequest(odd, Path.Combine(Path.GetTempPath(), "x", "videoprep.exe"), new string('a', 64), 77, "0.2.9", "0.3.0");
        Assert.True(ApplyRequest.TryParse(req.ToArguments(), req.Staged, out var back));
        Assert.Equal(req, back);
        Assert.False(ApplyRequest.TryParse(["--apply-update", "--target", "relative.exe", "--sha256", new string('a', 64), "--pid", "1", "--from", "a", "--to", "b"], "s", out _));
        Assert.False(ApplyRequest.TryParse(["--apply-update", "--target", req.Target, "--sha256", "short", "--pid", "1", "--from", "a", "--to", "b"], "s", out _));
        Assert.False(ApplyRequest.TryParse(["--apply-update", "--target", req.Target, "--sha256", new string('a', 64), "--pid", "0", "--from", "a", "--to", "b"], "s", out _));
        Assert.False(ApplyRequest.TryParse([], "s", out _));
        Assert.False(ApplyRequest.TryParse(["--other"], "s", out _));
    }

    [Fact]
    public void The_result_is_saved_for_the_next_start_and_read_once()
    {
        var folder = Path.Combine(_dir, "updates");
        Assert.Null(UpdateResult.Take(folder));
        UpdateResult.Write(folder, new UpdateResult(false, "0.2.9", "0.3.0", UpdateFailure.FilesLocked, "busy"));
        var r = UpdateResult.Take(folder)!;
        Assert.Equal((false, "0.2.9", "0.3.0", UpdateFailure.FilesLocked, "busy"), (r.Ok, r.From, r.To, r.Failure, r.Detail));
        Assert.Null(UpdateResult.Take(folder));
        File.WriteAllText(Path.Combine(folder, UpdateResult.FileName), "{ not json");
        Assert.Null(UpdateResult.Take(folder));                                              // a damaged file is dropped, never a crash
        Assert.False(File.Exists(Path.Combine(folder, UpdateResult.FileName)));
    }

    [Fact]
    public void A_folder_the_user_cannot_write_to_is_found_out_before_downloading_and_without_asking_for_administrator()
    {
        var ok = Path.Combine(_dir, "writable");
        Directory.CreateDirectory(ok);
        Assert.Null(UpdatePermissions.Check(Path.Combine(ok, "videoprep.exe")));
        Assert.Equal(UpdateFailure.NoInstaller, UpdatePermissions.Check(Path.Combine(ok, "dotnet.exe")));      // run by something else (development)
        Assert.Equal(UpdateFailure.NoInstaller, UpdatePermissions.Check(null));
        Assert.Equal(UpdateFailure.NoWritePermission, UpdatePermissions.Check(Path.Combine(_dir, "does-not-exist", "videoprep.exe")));

        if (!OperatingSystem.IsWindows()) return;
        // A real folder of ours with writing denied by Windows itself (icacls), as Program Files would be for a normal user.
        var locked = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(locked);
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        Run("icacls", locked, "/deny", user + ":(OI)(CI)(W,WD,AD,WEA,WA)");
        try { Assert.Equal(UpdateFailure.NoWritePermission, UpdatePermissions.Check(Path.Combine(locked, "videoprep.exe"))); }
        finally { Run("icacls", locked, "/remove:d", user); }
    }

    private static void Run(params string[] cmd)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(cmd[0]) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
    }
}
