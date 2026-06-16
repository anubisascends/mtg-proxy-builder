using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    /// <summary>
    /// Stores duplex alignment calibration offsets for a specific printer.
    /// Offsets are applied to back pages only — the front page is the reference.
    /// Positive X shifts back page content to the right; positive Y shifts down.
    /// Legacy single-offset fields are retained for backward compatibility.
    /// </summary>
    public class PrinterProfile
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "Default";

        /// <summary>Legacy single X offset (mm). Kept for backward compatibility.</summary>
        [JsonProperty("offsetXMm")]
        public float OffsetXMm { get; set; }

        /// <summary>Legacy single Y offset (mm). Kept for backward compatibility.</summary>
        [JsonProperty("offsetYMm")]
        public float OffsetYMm { get; set; }

        // ── 4-corner offsets (mm) ──

        [JsonProperty("offsetTLXMm")]
        public float OffsetTLXMm { get; set; }

        [JsonProperty("offsetTLYMm")]
        public float OffsetTLYMm { get; set; }

        [JsonProperty("offsetTRXMm")]
        public float OffsetTRXMm { get; set; }

        [JsonProperty("offsetTRYMm")]
        public float OffsetTRYMm { get; set; }

        [JsonProperty("offsetBLXMm")]
        public float OffsetBLXMm { get; set; }

        [JsonProperty("offsetBLYMm")]
        public float OffsetBLYMm { get; set; }

        [JsonProperty("offsetBRXMm")]
        public float OffsetBRXMm { get; set; }

        [JsonProperty("offsetBRYMm")]
        public float OffsetBRYMm { get; set; }

        /// <summary>
        /// If all 4-corner offsets are zero but legacy OffsetXMm/OffsetYMm are non-zero,
        /// copies the legacy values to every corner so the old calibration is preserved.
        /// Call this after deserialization.
        /// </summary>
        public void MigrateLegacyOffsets()
        {
            bool allCornersZero =
                OffsetTLXMm == 0 && OffsetTLYMm == 0 &&
                OffsetTRXMm == 0 && OffsetTRYMm == 0 &&
                OffsetBLXMm == 0 && OffsetBLYMm == 0 &&
                OffsetBRXMm == 0 && OffsetBRYMm == 0;

            bool hasLegacy = OffsetXMm != 0 || OffsetYMm != 0;

            if (allCornersZero && hasLegacy)
            {
                OffsetTLXMm = OffsetXMm;
                OffsetTLYMm = OffsetYMm;
                OffsetTRXMm = OffsetXMm;
                OffsetTRYMm = OffsetYMm;
                OffsetBLXMm = OffsetXMm;
                OffsetBLYMm = OffsetYMm;
                OffsetBRXMm = OffsetXMm;
                OffsetBRYMm = OffsetYMm;
            }
        }

        public override string ToString() => Name;
    }
}
