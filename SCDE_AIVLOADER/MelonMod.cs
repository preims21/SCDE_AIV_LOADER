using MelonLoader;
using HarmonyLib;

[assembly: MelonInfo(typeof(SCDE_AIVLOADER.AivLoaderMod), "Stronghold Crusader DE Custom aivJSON Loader", "1.0.0", "preims21")]
[assembly: MelonGame(null, null)]

namespace SCDE_AIVLOADER
{

    public sealed class AivLoaderMod : MelonMod
    {
        private HarmonyLib.Harmony _harmony;

        public override void OnInitializeMelon()
        {
            _harmony = new HarmonyLib.Harmony("com.preims21.scde.aivloader");
            _harmony.PatchAll();

            LoggerInstance.Msg("Initialized + patched!");
        }

        public override void OnDeinitializeMelon()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();

            LoggerInstance.Msg("Unpatched.");
        }
    }
}