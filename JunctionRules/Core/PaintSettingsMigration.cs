namespace JunctionRules.Core
{
    /// <summary>
    /// 0.3.5-probe (JR-T-003e): one-time settings migration for "Yellow box paint test: paint onto".
    /// Roads (2) is the default (live 15 Nov 2026: Roads + Other (34) also paints poles and signs; Roads alone still
    /// paints the road). The 0.3.0-0.3.4 probe kit (step S6) told the owner to switch to Roads + Other if nothing showed,
    /// so saved settings from that test hold 34. The migration moves exactly that value (34) back to Roads once; any
    /// other saved value is the owner's own choice and is kept. After the migration Roads + Other stays selectable.
    /// </summary>
    public static class PaintSettingsMigration
    {
        /// <summary>Settings schema version this build writes (0 = saved by 0.3.4-probe or older).</summary>
        public const int Current = 1;

        /// <summary>The value the old probe step left behind (DecalLayers Roads 2 + Other 32).</summary>
        public const BoxPaintLayers OldProbeValue = BoxPaintLayers.RoadsAndOther;

        public const BoxPaintLayers Default = BoxPaintLayers.Roads;

        /// <summary>Returns the layers to use after loading; 'changed' when the stored value was migrated.</summary>
        public static BoxPaintLayers Migrate(int storedVersion, BoxPaintLayers stored, out bool changed)
        {
            changed = false;
            if (storedVersion >= Current) return stored;
            if (stored == OldProbeValue) { changed = true; return Default; }
            return stored;
        }
    }
}
