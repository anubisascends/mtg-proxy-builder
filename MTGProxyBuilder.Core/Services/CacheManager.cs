namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Manages all cache directories. Provides size calculation, cleanup on startup,
    /// and manual clear for the user.
    /// </summary>
    public class CacheManager
    {
        private readonly string _rootDir;
        private readonly string _imageCacheDir;
        private readonly string _extractedProjectsDir;
        private readonly string _bleedCacheDir;

        // These are user data, NOT temp — excluded from auto-cleanup
        private readonly string _backArtLibraryDir;

        public CacheManager()
        {
            _rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder");

            _imageCacheDir = Path.Combine(_rootDir, "ImageCache");
            _extractedProjectsDir = Path.Combine(_rootDir, "ExtractedProjects");
            _bleedCacheDir = Path.Combine(_rootDir, "BleedCache");
            _backArtLibraryDir = Path.Combine(_rootDir, "BackArtLibrary");
        }

        /// <summary>
        /// Runs on app startup. Removes stale extracted project data and old bleed cache.
        /// </summary>
        public void CleanupOnStartup()
        {
            try
            {
                // Clear all extracted projects — they'll be re-extracted when a project is opened
                ClearDirectory(_extractedProjectsDir);

                // Clear bleed cache — regenerated on demand
                ClearDirectory(_bleedCacheDir);

                // Clean up any orphaned .tmp files from failed saves
                if (Directory.Exists(_rootDir))
                {
                    foreach (var tmp in Directory.GetFiles(_rootDir, "*.tmp", SearchOption.AllDirectories))
                    {
                        try { File.Delete(tmp); } catch { }
                    }
                }
            }
            catch { } // startup cleanup should never crash the app
        }

        /// <summary>
        /// Clears all non-essential caches (image cache, bleed cache, extracted projects).
        /// Does NOT touch the back art library or favorites.
        /// </summary>
        public (int filesDeleted, long bytesFreed) ClearAllCaches()
        {
            int files = 0;
            long bytes = 0;

            var (f, b) = ClearDirectory(_imageCacheDir);
            files += f; bytes += b;

            (f, b) = ClearDirectory(_bleedCacheDir);
            files += f; bytes += b;

            (f, b) = ClearDirectory(_extractedProjectsDir);
            files += f; bytes += b;

            return (files, bytes);
        }

        /// <summary>Returns the total size of all cache directories in bytes.</summary>
        public long GetTotalCacheSizeBytes()
        {
            return GetDirectorySize(_imageCacheDir)
                 + GetDirectorySize(_bleedCacheDir)
                 + GetDirectorySize(_extractedProjectsDir);
        }

        /// <summary>Formats bytes as a human-readable string.</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private static (int filesDeleted, long bytesFreed) ClearDirectory(string path)
        {
            int files = 0;
            long bytes = 0;

            if (!Directory.Exists(path)) return (0, 0);

            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        bytes += info.Length;
                        info.Delete();
                        files++;
                    }
                    catch { } // skip locked files
                }

                // Remove empty subdirectories
                foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)) // deepest first
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(dir).Length == 0)
                            Directory.Delete(dir);
                    }
                    catch { }
                }
            }
            catch { }

            return (files, bytes);
        }

        private static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            try
            {
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }
            catch { return 0; }
        }
    }
}
