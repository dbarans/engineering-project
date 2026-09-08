/// <summary>
/// Which mixer group a sound comes out of. Set per entry in <see cref="SoundBank"/> and
/// resolved to a real <c>AudioMixerGroup</c> by <see cref="AudioMixerService"/>.
///
/// <c>Sfx</c> is deliberately 0 rather than first-by-alphabet or first-by-importance: the
/// bank asset was serialized before this field existed, so every pre-existing entry
/// deserializes with the default 0 and has to land on the group it was already playing
/// through. Renumbering this enum silently re-routes every sound in the bank.
/// </summary>
public enum AudioChannel
{
    /// <summary>Everything diegetic — footsteps, doors, combat, the world.</summary>
    Sfx = 0,

    /// <summary>Music and ambience. Nothing plays here yet; the group exists so it can.</summary>
    Music = 1,

    /// <summary>Interface feedback: clicks and anything else that is not in the world.</summary>
    Ui = 2,

    /// <summary>
    /// The parent of the other three. Valid as an output, but an entry that picks it is
    /// opting out of ever being turned down separately — it is here for the volume API,
    /// which needs a name for "everything at once".
    /// </summary>
    Master = 3,
}
