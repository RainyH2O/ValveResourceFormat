using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using Sigstore;

namespace GUI.Utils;

/// <summary>
/// Downloads the build offered by <see cref="UpdateChecker"/>, verifies it against the manifest
/// and against the build provenance attested by the CI, and swaps it in place of the running executable.
/// </summary>
static class UpdateInstaller
{
    private const string ReplacedSuffix = ".old";
    private const string Repository = "ValveResourceFormat/ValveResourceFormat";
    private const string Workflow = ".github/workflows/build.yml";

    // Keeps the Sigstore trust root cached between verifications
    private static readonly SigstoreVerifier Verifier = new();

    // A single file publish has nothing but the bundle on disk. A debug build has the managed
    // assembly next to its apphost, and cannot be replaced by a single downloaded file.
    private static bool IsSingleFileBundle(string exePath) => !File.Exists(Path.ChangeExtension(exePath, ".dll"));

    /// <summary>
    /// Removes the executable that a previous update replaced.
    /// </summary>
    public static void CleanupPreviousInstall()
    {
        var exePath = Environment.ProcessPath;

        if (exePath == null)
        {
            return;
        }

        try
        {
            File.Delete(exePath + ReplacedSuffix);
        }
        catch (IOException)
        {
            // The previous instance may still be exiting, it will be gone by the next launch
        }
        catch (UnauthorizedAccessException)
        {
            //
        }
    }

    /// <summary>
    /// Downloads and installs the offered build behind a progress dialog.
    /// Returns false when self updating is not possible so the caller can fall back to the website.
    /// </summary>
    public static async Task<bool> InstallAsync(IWin32Window owner)
    {
        var url = UpdateChecker.DownloadUrl;
        var expectedHash = UpdateChecker.DownloadSha256;
        var exePath = Environment.ProcessPath;

        if (url == null || expectedHash == null || exePath == null || !OperatingSystem.IsWindows())
        {
            return false;
        }

        // A random name in the user's own temp folder cannot be planted ahead of time by anyone else
        var downloadPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var downloaded = false;

        using (var dialog = new GenericProgressForm { Text = $"Downloading {UpdateChecker.NewVersionText}" })
        {
            dialog.OnProcess = async cancellationToken =>
            {
                using var httpClient = new HttpClient
                {
                    // Downloads can take a while on a slow connection, the dialog's cancel button is the way out
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"Source2Viewer/{Program.ProductVersion} (+https://github.com/{Repository})");

                try
                {
                    var (size, hash) = await DownloadAsync(httpClient, url, downloadPath, dialog, cancellationToken).ConfigureAwait(false);

                    dialog.SetProgress("Verifying…");
                    Verify(downloadPath, size, hash, expectedHash);

                    dialog.SetProgress("Verifying build provenance…");
                    await VerifyProvenanceAsync(httpClient, hash, cancellationToken).ConfigureAwait(false);

                    downloaded = true;
                }
                catch
                {
                    File.Delete(downloadPath);
                    throw;
                }
            };

            await dialog.ShowDialogAsync(owner).ConfigureAwait(true);
            await dialog.WorkCompletion.ConfigureAwait(true);
        }

        if (!downloaded)
        {
            return true;
        }

        if (!IsSingleFileBundle(exePath))
        {
            // Nothing to swap for a non bundled build, leave the verified file for inspection
            await AppMessageDialogs.ShowMessageAsync(
                $"The update was downloaded and verified, but this build cannot replace itself.{Environment.NewLine}{Environment.NewLine}{downloadPath}",
                "Update downloaded").ConfigureAwait(true);

            return true;
        }

        Swap(exePath, downloadPath);

        var restartButton = new TaskDialogButton("Restart now");
        var page = new TaskDialogPage
        {
            Caption = "Update installed",
            Heading = $"Source 2 Viewer {UpdateChecker.NewVersionText} has been installed",
            Text = "It will be used the next time the viewer starts.",
            Icon = TaskDialogIcon.ShieldSuccessGreenBar,
            Buttons = { restartButton, new TaskDialogButton("Later") },
            DefaultButton = restartButton,
        };

        if (await TaskDialog.ShowDialogAsync(owner, page).ConfigureAwait(true) == restartButton)
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            Program.MainForm.Close();
        }

        return true;
    }

    // Hashes and counts what passes through on the way to the file, so the download is verified
    // without reading it back from disk and the progress dialog is fed from the same stream.
    private sealed class HashingProgressStream(Stream inner, Action<long> onProgress) : Stream
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long BytesWritten { get; private set; }

        public string GetHash() => Convert.ToHexStringLower(hash.GetHashAndReset());

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Advance(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            Advance(buffer.Span);
        }

        private void Advance(ReadOnlySpan<byte> buffer)
        {
            hash.AppendData(buffer);
            BytesWritten += buffer.Length;
            onProgress(BytesWritten);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hash.Dispose();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private static async Task<(long Size, string Hash)> DownloadAsync(HttpClient httpClient, string url, string downloadPath, GenericProgressForm dialog, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? UpdateChecker.DownloadSize ?? 0;
        var totalMegabytes = totalBytes / 1024f / 1024f;

        if (totalBytes > 0)
        {
            dialog.SetBarMax(1000);
        }

        void ReportProgress(long written)
        {
            if (totalBytes > 0)
            {
                dialog.SetBarValue((int)(written * 1000 / totalBytes));
                dialog.SetProgress($"{written / 1024f / 1024f:F1} MB of {totalMegabytes:F1} MB");
            }
            else
            {
                dialog.SetProgress($"{written / 1024f / 1024f:F1} MB");
            }
        }

        using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var file = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
        using var destination = new HashingProgressStream(file, ReportProgress);

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        return (destination.BytesWritten, destination.GetHash());
    }

    // The manifest server only tells us which file to fetch. Whether that file really came out of this
    // repository's CI is proven by the provenance attestation GitHub stores for its hash, which is signed
    // through Sigstore with the identity of the workflow run that produced it.
    private static async Task VerifyProvenanceAsync(HttpClient httpClient, string hash, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri($"https://api.github.com/repos/{Repository}/attestations/sha256:{hash}"), cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidDataException("No build provenance was found for the downloaded file.");
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);

        // Dev builds are attested on the branch, releases on their tag
        var expectedRef = UpdateChecker.IsNewVersionStableBuild ? $"refs/tags/{UpdateChecker.NewVersion}" : "refs/heads/master";
        var policy = new VerificationPolicy
        {
            CertificateIdentity = new CertificateIdentity
            {
                Issuer = "https://token.actions.githubusercontent.com",
                SubjectAlternativeName = $"https://github.com/{Repository}/{Workflow}@{expectedRef}",
                Extensions = new CertificateExtensionPolicy
                {
                    SourceRepositoryUri = $"https://github.com/{Repository}",
                    SourceRepositoryRef = expectedRef,
                    RunnerEnvironment = "github-hosted",
                },
            },
        };

        var hashBytes = Convert.FromHexString(hash);
        string? failure = null;

        foreach (var attestation in document.RootElement.GetProperty("attestations").EnumerateArray())
        {
            var bundle = SigstoreBundle.Deserialize(attestation.GetProperty("bundle").GetRawText());

            // The library proves who signed the statement, the statement itself must still name our file
            var statement = bundle.DsseEnvelope?.GetStatement();

            if (statement?.Subject.Any(subject => subject.Digest.TryGetValue("sha256", out var digest) && digest.Equals(hash, StringComparison.OrdinalIgnoreCase)) != true)
            {
                failure = "The attestation does not describe the downloaded file.";
                continue;
            }

            var (success, result) = await Verifier.TryVerifyDigestAsync(hashBytes, HashAlgorithmType.Sha256, bundle, policy, cancellationToken).ConfigureAwait(false);

            if (success)
            {
                Log.Info(nameof(UpdateInstaller), $"Verified build provenance signed by {result?.SignerIdentity}");
                return;
            }

            failure = result?.FailureReason;
        }

        throw new InvalidDataException($"The build provenance of the downloaded file could not be verified. {failure}");
    }

    private static void Verify(string downloadPath, long actualSize, string actualHash, string expectedHash)
    {
        var expectedSize = UpdateChecker.DownloadSize;

        if (expectedSize != null && actualSize != expectedSize)
        {
            throw new InvalidDataException($"Downloaded {actualSize} bytes but expected {expectedSize} bytes.");
        }

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded file does not match the expected hash.");
        }

        // The file is what the manifest promised, now make sure the manifest promised the right build
        var fileVersion = FileVersionInfo.GetVersionInfo(downloadPath).FileVersion;

        if (!Version.TryParse(fileVersion, out var version) || !Version.TryParse(UpdateChecker.NewVersion, out var expectedVersion))
        {
            // Dev builds are identified by build number alone
            if (!int.TryParse(UpdateChecker.NewVersion, out var expectedBuild) || version?.Build != expectedBuild)
            {
                throw new InvalidDataException($"The downloaded file reports version {fileVersion} instead of {UpdateChecker.NewVersion}.");
            }

            return;
        }

        if (version.Major != expectedVersion.Major || version.Minor != expectedVersion.Minor)
        {
            throw new InvalidDataException($"The downloaded file reports version {fileVersion} instead of {UpdateChecker.NewVersion}.");
        }
    }

    // Windows allows renaming a running executable, only deleting or overwriting it is refused.
    // The old file is removed on the next launch, once nothing is running from it anymore.
    private static void Swap(string exePath, string downloadPath)
    {
        var replacedPath = exePath + ReplacedSuffix;

        try
        {
            File.Delete(replacedPath);
        }
        catch (IOException e)
        {
            throw new IOException("A previous update is still in use, restart the viewer and try again.", e);
        }

        File.Move(exePath, replacedPath);

        try
        {
            File.Move(downloadPath, exePath);
        }
        catch
        {
            File.Move(replacedPath, exePath);
            throw;
        }
    }
}
