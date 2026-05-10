using System.IO.Compression;
using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Saves/loads projects as self-contained ZIP archives (.mtgproj).
    /// Layout inside the archive:
    ///   project.json          — project metadata + card list (image paths are relative)
    ///   images/{filename}     — every referenced artwork file
    /// </summary>
    public class ProjectSerializationService
    {
        private const string ProjectJsonEntry = "project.json";
        private const string ImageFolder = "images/";
        private const int ProjectFileVersion = 2;

        // Temp directory for extracted images so the app can display them
        private readonly string _extractRoot;

        public ProjectSerializationService()
        {
            _extractRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "ExtractedProjects");
            Directory.CreateDirectory(_extractRoot);
        }

        // ================================================================
        //  SAVE
        // ================================================================

        public async Task<bool> SaveProjectAsync(ProjectModel project, string filePath)
        {
            try
            {
                // Work on a temp file so we don't corrupt the original on failure
                string tempPath = filePath + ".tmp";

                await Task.Run(() =>
                {
                    using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

                    // Collect every unique image path and map to archive-relative names
                    var imageMap = BuildImageMap(project);

                    // Write images into the archive
                    foreach (var (absolutePath, archiveName) in imageMap)
                    {
                        if (!File.Exists(absolutePath)) continue;

                        var entry = zip.CreateEntry(ImageFolder + archiveName, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        using var fileStream = File.OpenRead(absolutePath);
                        fileStream.CopyTo(entryStream);
                    }

                    // Build a serializable copy with relative paths
                    var wrapper = new ProjectFileWrapper
                    {
                        Version = ProjectFileVersion,
                        Project = CloneWithRelativePaths(project, imageMap)
                    };

                    string json = JsonConvert.SerializeObject(wrapper, Formatting.Indented);
                    var jsonEntry = zip.CreateEntry(ProjectJsonEntry, CompressionLevel.Optimal);
                    using var jsonStream = new StreamWriter(jsonEntry.Open());
                    jsonStream.Write(json);
                });

                // Atomic replace
                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(tempPath, filePath);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save project error: {ex.Message}");
                return false;
            }
        }

        // ================================================================
        //  LOAD
        // ================================================================

        public async Task<ProjectModel?> LoadProjectAsync(string filePath)
        {
            try
            {
                return await Task.Run(() =>
                {
                    using var stream = File.OpenRead(filePath);
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

                    // Read project.json
                    var jsonEntry = zip.GetEntry(ProjectJsonEntry);
                    if (jsonEntry == null) return null;

                    string json;
                    using (var reader = new StreamReader(jsonEntry.Open()))
                        json = reader.ReadToEnd();

                    var wrapper = JsonConvert.DeserializeObject<ProjectFileWrapper>(json);
                    if (wrapper?.Project == null) return null;

                    var project = wrapper.Project;

                    // Extract images to a temp folder unique to this file
                    string projectHash = Path.GetFileNameWithoutExtension(filePath)
                        + "_" + filePath.GetHashCode().ToString("X8");
                    string extractDir = Path.Combine(_extractRoot, projectHash, "images");

                    // Clean previous extraction
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, true);
                    Directory.CreateDirectory(extractDir);

                    foreach (var entry in zip.Entries)
                    {
                        if (!entry.FullName.StartsWith(ImageFolder) || entry.FullName == ImageFolder)
                            continue;

                        string destPath = Path.Combine(extractDir, entry.Name);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }

                    // Resolve relative paths back to absolute extracted paths
                    foreach (var card in project.Cards)
                    {
                        card.ArtworkPath = ResolveImagePath(card.ArtworkPath, extractDir);
                        card.BackArtworkPath = ResolveImagePath(card.BackArtworkPath, extractDir);
                    }

                    return project;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load project error: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        /// <summary>
        /// Builds a map from absolute file path → archive filename for every
        /// unique image referenced by the project's cards.
        /// </summary>
        private Dictionary<string, string> BuildImageMap(ProjectModel project)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int counter = 0;

            void Track(string? path)
            {
                if (string.IsNullOrEmpty(path) || map.ContainsKey(path)) return;
                string ext = Path.GetExtension(path);
                string archiveName = $"{counter++:D4}_{SanitizeFileName(Path.GetFileName(path))}";
                // Ensure uniqueness even if file names collide
                if (map.Values.Contains(archiveName))
                    archiveName = $"{counter++:D4}_{Guid.NewGuid():N}{ext}";
                map[path] = archiveName;
            }

            foreach (var card in project.Cards)
            {
                Track(card.ArtworkPath);
                Track(card.BackArtworkPath);
            }

            return map;
        }

        /// <summary>
        /// Returns a deep-ish copy of the project with card image paths
        /// replaced by archive-relative names (e.g. "images/0001_art.jpg").
        /// </summary>
        private ProjectModel CloneWithRelativePaths(ProjectModel source,
            Dictionary<string, string> imageMap)
        {
            string Rel(string? absolutePath)
            {
                if (string.IsNullOrEmpty(absolutePath)) return string.Empty;
                return imageMap.TryGetValue(absolutePath, out var archiveName)
                    ? ImageFolder + archiveName
                    : string.Empty;
            }

            return new ProjectModel
            {
                ProjectId = source.ProjectId,
                ProjectName = source.ProjectName,
                PageSettings = source.PageSettings,
                PrintSettings = source.PrintSettings,
                CreatedDate = source.CreatedDate,
                LastModified = DateTime.Now,
                Cards = source.Cards.Select(c => new CardModel
                {
                    CardId = c.CardId,
                    Name = c.Name,
                    ArtworkPath = Rel(c.ArtworkPath),
                    BackArtworkPath = string.IsNullOrEmpty(c.BackArtworkPath) ? null : Rel(c.BackArtworkPath),
                    ScryfallId = c.ScryfallId,
                    Quantity = c.Quantity,
                    IncludeBack = c.IncludeBack
                }).ToList()
            };
        }

        /// <summary>
        /// Converts an archive-relative path (e.g. "images/0001_art.jpg")
        /// back to an absolute path in the extraction directory, or returns
        /// the path as-is if it's already absolute and exists.
        /// </summary>
        private string ResolveImagePath(string? relativePath, string extractDir)
        {
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;

            // Already an absolute path that exists (e.g. from an older format)
            if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
                return relativePath;

            // Strip the "images/" prefix if present
            string fileName = relativePath.StartsWith(ImageFolder)
                ? relativePath[ImageFolder.Length..]
                : Path.GetFileName(relativePath);

            string candidate = Path.Combine(extractDir, fileName);
            return File.Exists(candidate) ? candidate : string.Empty;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        // ================================================================
        //  INTERNAL WRAPPER
        // ================================================================

        private class ProjectFileWrapper
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("project")]
            public ProjectModel? Project { get; set; }
        }
    }
}
