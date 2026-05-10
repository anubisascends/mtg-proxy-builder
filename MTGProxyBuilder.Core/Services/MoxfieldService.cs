using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MTGProxyBuilder.Core.Services
{
    public class MoxfieldDeckEntry
    {
        public string CardName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Board { get; set; } = string.Empty;
        public string? ScryfallId { get; set; }
    }

    public class MoxfieldDeck
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public List<MoxfieldDeckEntry> Entries { get; set; } = new();
    }

    public class MoxfieldService
    {
        /// <summary>
        /// Extracts the deck public ID from a Moxfield URL.
        /// </summary>
        public static string? ParseDeckId(string url)
        {
            url = url.Trim();

            if (!url.Contains('/') && !url.Contains('.'))
                return url;

            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                int decksIdx = Array.IndexOf(segments, "decks");
                if (decksIdx >= 0 && decksIdx + 1 < segments.Length)
                    return segments[decksIdx + 1];
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Fetches deck data from Moxfield. Uses curl to bypass Cloudflare TLS fingerprinting.
        /// </summary>
        public async Task<(MoxfieldDeck? Deck, string? Error)> FetchDeckAsync(string deckId)
        {
            try
            {
                string apiUrl = $"https://api2.moxfield.com/v2/decks/all/{deckId}";

                var json = await FetchWithCurlAsync(apiUrl);
                if (json == null)
                    return (null, "Failed to fetch deck. Make sure curl is installed and the deck URL is correct.");

                // Check if we got HTML instead of JSON (Cloudflare block)
                if (json.TrimStart().StartsWith('<'))
                    return (null, "Moxfield returned an HTML page instead of JSON. The deck may be private or the URL may be wrong.");

                var root = JObject.Parse(json);

                var deck = new MoxfieldDeck
                {
                    Name = root["name"]?.ToString() ?? "Imported Deck",
                    Format = root["format"]?.ToString() ?? "unknown"
                };

                string[] boards = { "mainboard", "sideboard", "commanders", "companions" };
                foreach (var board in boards)
                {
                    var section = root[board] as JObject;
                    if (section == null) continue;

                    foreach (var prop in section.Properties())
                    {
                        var entry = prop.Value;
                        var cardObj = entry?["card"];

                        deck.Entries.Add(new MoxfieldDeckEntry
                        {
                            CardName = prop.Name,
                            Quantity = entry?["quantity"]?.Value<int>() ?? 1,
                            Board = board,
                            ScryfallId = cardObj?["scryfall_id"]?.ToString()
                                      ?? cardObj?["scryfallId"]?.ToString()
                        });
                    }
                }

                return (deck, null);
            }
            catch (JsonException)
            {
                return (null, "Failed to parse Moxfield response. The deck may be private or the URL may be invalid.");
            }
            catch (Exception ex)
            {
                return (null, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Uses curl (available on Windows 10+) to make HTTP requests,
        /// bypassing Cloudflare's TLS fingerprinting of .NET HttpClient.
        /// </summary>
        private async Task<string?> FetchWithCurlAsync(string url)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl",
                    Arguments = $"-s -L -H \"Accept: application/json\" -H \"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36\" \"{url}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
