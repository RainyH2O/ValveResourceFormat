//#define TEST_NON_LOCAL_BUILD // Pretend to have been built on a CI as a dev version

using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Utils;

static partial class UpdateChecker
{
    /// <summary>
    /// A downloadable file for one runtime identifier.
    /// </summary>
    public class UpdateAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    /// <summary>
    /// The latest tagged release.
    /// </summary>
    public class StableUpdate
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("releaseNotesUrl")]
        public string? ReleaseNotesUrl { get; set; }

        [JsonPropertyName("assets")]
        public Dictionary<string, UpdateAsset>? Assets { get; set; }
    }

    /// <summary>
    /// The latest automated build of the master branch.
    /// </summary>
    public class DevUpdate
    {
        [JsonPropertyName("buildNumber")]
        public int BuildNumber { get; set; }

        [JsonPropertyName("assets")]
        public Dictionary<string, UpdateAsset>? Assets { get; set; }
    }

    /// <summary>
    /// The update manifest describing every channel we can offer.
    /// </summary>
    public class UpdateManifest
    {
        [JsonPropertyName("stable")]
        public StableUpdate? Stable { get; set; }

        [JsonPropertyName("dev")]
        public DevUpdate? Dev { get; set; }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(UpdateManifest))]
    partial class SourceGenerationContext : JsonSerializerContext
    {
    }

    private const string ManifestUrl = "https://update.s2v.app/v1/latest.json";

    // The manifest describes both channels, so it is fetched once and re-evaluated when the channel changes
    private static Task<UpdateManifest?>? ManifestTask;
    private static readonly Lock CheckLock = new();
    public static bool IsNewVersionAvailable { get; private set; }
    public static bool IsNewVersionStableBuild { get; private set; }
    /// <summary>Whether the offered version is from a different channel than the running build, rather than a newer build of the same channel.</summary>
    public static bool IsChannelSwitch { get; private set; }
    public static string? NewVersion { get; private set; }
    public static string? ReleaseNotesUrl { get; private set; }
    public static string? ReleaseNotesVersion { get; private set; }
    public static string? DownloadUrl { get; private set; }
    public static long? DownloadSize { get; private set; }
    public static string? DownloadSha256 { get; private set; }

    public static async Task CheckForUpdates()
    {
        Task<UpdateManifest?> manifestTask;

        using (CheckLock.EnterScope())
        {
            manifestTask = ManifestTask ??= GetManifestAsync();
        }

        var manifest = await manifestTask.ConfigureAwait(false);

        Evaluate(manifest, Settings.Config.Update.Channel);
    }

    /// <summary>
    /// Switches the update channel. The next check evaluates the already fetched manifest against the new channel.
    /// </summary>
    public static void SetChannel(Settings.UpdateChannel channel)
    {
        Settings.Config.Update.Channel = channel;
        Settings.Config.Update.UpdateAvailable = false;
    }

    /// <summary>
    /// Performs the automatic update check if it is enabled and has not been performed recently.
    /// Returns true if a new version is available, either remembered from an earlier check or found by checking now.
    /// </summary>
    public static async Task<bool> CheckForUpdatesIfNecessary()
    {
        if (!Settings.Config.Update.CheckAutomatically)
        {
            return false;
        }

        if (Settings.Config.Update.UpdateAvailable)
        {
            return true;
        }

        var now = DateTime.UtcNow;

        if (DateTime.TryParseExact(Settings.Config.Update.LastCheck, "s", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastCheck))
        {
            var diff = now.Subtract(lastCheck);

            // Perform auto update check once a day
            if (diff.TotalDays < 1)
            {
                return false;
            }
        }

        Settings.Config.Update.LastCheck = now.ToString("s", CultureInfo.InvariantCulture);

        // Offloaded so that the request setup does not run on the ui thread
        await Task.Run(CheckForUpdates).ConfigureAwait(false);

        return IsNewVersionAvailable;
    }

    private static Version GetCurrentVersion()
    {
        var version = Program.ProductVersion;
        var versionPlus = version.IndexOf('+', StringComparison.InvariantCulture); // Drop the git commit

        return new Version(versionPlus > 0 ? version[..versionPlus] : version);
    }

    private static bool IsLocalBuild(Version currentVersion)
    {
#if TEST_NON_LOCAL_BUILD
        return false;
#else
        return currentVersion.Build == 0;
#endif
    }

    private static void Evaluate(UpdateManifest? manifest, Settings.UpdateChannel channel)
    {
        var currentVersion = GetCurrentVersion();

        if (IsLocalBuild(currentVersion))
        {
            Settings.Config.Update.UpdateAvailable = false;
            IsNewVersionAvailable = false;
            IsNewVersionStableBuild = true; // So that the label does not read as a dev build
            NewVersion = ":)";
            return; // This was not built on the CI
        }

        if (manifest == null)
        {
            return; // The fetch failed and the error has been shown
        }

        var stable = manifest.Stable;
        var stableVersion = stable?.Version ?? "0.0";

        // Release notes are always for the stable release, no matter which channel is selected
        ReleaseNotesUrl = stable?.ReleaseNotesUrl;
        ReleaseNotesVersion = stableVersion;

        IsNewVersionStableBuild = channel == Settings.UpdateChannel.Stable;
        IsChannelSwitch = channel != Program.BuildChannel;

        // Switching channels always offers that channel's latest build, even when it is older than the running one
        if (IsChannelSwitch)
        {
            IsNewVersionAvailable = true;
        }
        else if (IsNewVersionStableBuild)
        {
            var releaseVersion = Version.TryParse(stableVersion, out var parsed) ? parsed : new Version(0, 0);
            IsNewVersionAvailable = releaseVersion > new Version(currentVersion.Major, currentVersion.Minor);
        }
        else
        {
            IsNewVersionAvailable = (manifest.Dev?.BuildNumber ?? 0) > currentVersion.Build;
        }

        NewVersion = IsNewVersionStableBuild
            ? stableVersion
            : (manifest.Dev?.BuildNumber ?? 0).ToString(CultureInfo.InvariantCulture);

        var assets = IsNewVersionStableBuild ? stable?.Assets : manifest.Dev?.Assets;
        var asset = assets?.GetValueOrDefault(RuntimeInformation.RuntimeIdentifier);

        DownloadUrl = asset?.Url;
        DownloadSize = asset?.Size;
        DownloadSha256 = asset?.Sha256;

        if (Settings.Config.Update.CheckAutomatically)
        {
            Settings.Config.Update.UpdateAvailable = IsNewVersionAvailable;
        }
    }

    private static async Task<UpdateManifest?> GetManifestAsync()
    {
        if (IsLocalBuild(GetCurrentVersion()))
        {
            return null; // Local builds have nothing to compare against
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", $"Source2Viewer/{Program.ProductVersion} (+https://github.com/ValveResourceFormat/ValveResourceFormat)");

            var response = await httpClient.GetAsync(new Uri(ManifestUrl)).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var jsonStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            return await JsonSerializer.DeserializeAsync(jsonStream, SourceGenerationContext.Default.UpdateManifest).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error(nameof(UpdateChecker), $"Failed to check for updates: {e.Message}");

            await Program.MainForm.InvokeAsync(() =>
            {
                Program.ShowError(e);
            }).ConfigureAwait(false);

            return null;
        }
    }
}
