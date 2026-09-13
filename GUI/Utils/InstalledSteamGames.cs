using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace GUI.Utils;

/// <summary>
/// Installed Steam apps, found once in the background as the app starts.
/// </summary>
static class InstalledSteamGames
{
    private static Task<List<GameFolderLocator.SteamLibraryGameInfo>> findTask = FindGames();

    private static Task<List<GameFolderLocator.SteamLibraryGameInfo>> FindGames()
        => Task.Run(static () => GameFolderLocator.FindAllSteamGames().OrderBy(static game => game.AppID).ToList());

    /// <summary>
    /// Installed apps ordered by app id, or empty until <see cref="FindAsync"/> completes,
    /// so the UI never waits on reading Steam libraries from slow drives.
    /// </summary>
    public static IReadOnlyList<GameFolderLocator.SteamLibraryGameInfo> Games
    {
        get
        {
            var task = Volatile.Read(ref findTask);
            return task.IsCompletedSuccessfully ? task.Result : [];
        }
    }

    /// <summary>
    /// Starts finding the installed apps, and completes when they are found.
    /// </summary>
    public static Task<List<GameFolderLocator.SteamLibraryGameInfo>> FindAsync() => Volatile.Read(ref findTask);

    /// <summary>Clears the cached list and starts a fresh scan.</summary>
    public static void Refresh() => Volatile.Write(ref findTask, FindGames());
}
