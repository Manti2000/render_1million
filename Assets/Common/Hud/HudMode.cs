namespace MillionObjects
{
    /// <summary>
    /// Which read-out the HUD puts on screen. The interactive hotkey cycles the first three in
    /// declaration order; <see cref="StatusLine"/> is reserved for automatic runs.
    /// </summary>
    public enum HudMode
    {
        /// <summary>Nothing on screen, so the cloud can be filmed unobstructed.</summary>
        Hidden,
        /// <summary>Frame time, fps, the frame-time strip and the object counter only, for ramp-up footage.</summary>
        Framerate,
        /// <summary>Every read-out: counts, timing split, draw calls, memory, flexibility card and clock.</summary>
        Full,
        /// <summary>The benchmark's single status line, so every step renders the same tiny overlay.</summary>
        StatusLine
    }
}
