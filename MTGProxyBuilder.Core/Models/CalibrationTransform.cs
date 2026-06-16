namespace MTGProxyBuilder.Core.Models
{
    public record CalibrationTransform(
        float TranslateXPt,
        float TranslateYPt,
        float RotationDegrees)
    {
        private const float MmToPt = 72f / 25.4f;

        public bool HasCorrection =>
            Math.Abs(TranslateXPt) > 0.001f ||
            Math.Abs(TranslateYPt) > 0.001f ||
            Math.Abs(RotationDegrees) > 0.0001f;

        public static CalibrationTransform Compute(PrinterProfile profile, float gridWidthMm, float gridHeightMm)
        {
            float tlx = profile.OffsetTLXMm, tly = profile.OffsetTLYMm;
            float trx = profile.OffsetTRXMm, try_ = profile.OffsetTRYMm;
            float blx = profile.OffsetBLXMm, bly = profile.OffsetBLYMm;
            float brx = profile.OffsetBRXMm, bry = profile.OffsetBRYMm;

            float avgXMm = (tlx + trx + blx + brx) / 4f;
            float avgYMm = (tly + try_ + bly + bry) / 4f;

            float rightAvgY = (try_ + bry) / 2f;
            float leftAvgY = (tly + bly) / 2f;
            float horizAngleRad = gridWidthMm > 0
                ? (float)Math.Atan2(rightAvgY - leftAvgY, gridWidthMm) : 0;

            float topAvgX = (tlx + trx) / 2f;
            float bottomAvgX = (blx + brx) / 2f;
            float vertAngleRad = gridHeightMm > 0
                ? (float)Math.Atan2(topAvgX - bottomAvgX, gridHeightMm) : 0;

            float angleRad = (horizAngleRad + vertAngleRad) / 2f;
            float angleDeg = angleRad * (180f / (float)Math.PI);

            return new CalibrationTransform(avgXMm * MmToPt, avgYMm * MmToPt, angleDeg);
        }
    }
}
