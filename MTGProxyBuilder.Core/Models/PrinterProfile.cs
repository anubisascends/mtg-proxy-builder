using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    /// <summary>
    /// Stores duplex alignment calibration offsets for a specific printer.
    /// Offsets are applied to back pages only — the front page is the reference.
    /// Positive X shifts back page content to the right; positive Y shifts down.
    /// </summary>
    public class PrinterProfile
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "Default";

        [JsonProperty("offsetXMm")]
        public float OffsetXMm { get; set; }

        [JsonProperty("offsetYMm")]
        public float OffsetYMm { get; set; }

        public override string ToString() => Name;
    }
}
