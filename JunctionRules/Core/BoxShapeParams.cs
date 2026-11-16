namespace JunctionRules.Core
{
    /// <summary>
    /// JR-T-007b-r (F6): the shape parameters the paint and the tool's hover outline share (kerb inset, entrance length,
    /// marking margin), so the hover shows the same box the paint draws. Built from the paint list (paint-test.json, may
    /// be absent) and the Options margin; the list's "markingMargin" overrides the Options value while present, also
    /// when the Options slider moves afterwards (ApplySetting). Published at mod load, tool start and every paint-list
    /// read (Boxes/BoxStore.cs). Game-independent.
    /// </summary>
    public sealed class BoxShapeParams
    {
        public double EntranceLength = 8.0;
        public double KerbInset = 0.0;
        /// <summary>The list file's markingMargin; NaN = not in the file (the Options value applies).</summary>
        public double ListMarkingMargin = double.NaN;
        /// <summary>The margin in force.</summary>
        public double MarkingMargin = Core.MarkingMargin.Default;
        public string MarginSource = "options";

        /// <summary>From a parsed paint list (null = no list file) and the Options margin.</summary>
        public static BoxShapeParams From(PaintTestConfig list, double setting)
        {
            var p = new BoxShapeParams();
            if (list != null && list.Geometry != null)
            {
                p.EntranceLength = list.Geometry.EntranceLength;
                p.KerbInset = list.Geometry.KerbInset;
                p.ListMarkingMargin = list.Geometry.MarkingMargin;
            }
            p.ApplySetting(setting);
            return p;
        }

        /// <summary>The Options margin changed: the list override (if any) still wins.</summary>
        public void ApplySetting(double setting)
        {
            MarkingMargin = Core.MarkingMargin.Effective(setting, ListMarkingMargin, out string src);
            MarginSource = src;
        }

        public string Describe() => $"kerb inset +{KerbInset:0.00} m, entrance length {EntranceLength:0.0} m, marking margin {MarkingMargin:0.0} m ({MarginSource})";
    }
}
