using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using SCDE_AIVLOADER.IO;

namespace SCDE_AIVLOADER.Patches
{
    [HarmonyPatch]
    public class AIVLoader_getAIVData_Patch
    {
        // Adjust this if the type is actually namespaced, e.g. "GameNamespace.AIVLoader"
        private const string TargetTypeName = "AIVLoader";
        private const string TargetMethodName = "getAIVData";

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName(TargetTypeName);
            if (t == null)
            {
                MelonLogger.Error("Could not find type: " + TargetTypeName);
                return null;
            }

            var m = AccessTools.Method(t, TargetMethodName);
            if (m == null)
                MelonLogger.Error("Could not find method: " + TargetTypeName + "." + TargetMethodName);

            return m;
        }

        private static bool Prefix(int lord, int id, bool evreySkirmishSet, bool evreyHistoricalSet, ref short[] __result)
        {
            try
            {
                MelonLogger.Msg("Intercepted AIV load for " + lord + " " + id);
                if (evreyHistoricalSet || evreySkirmishSet)
                {
                    MelonLogger.Msg("Evrey-Set selected using builtin AIVs");
                    return true;
                }

                var basePath = Path.Combine(MelonEnvironment.GameRootDirectory, "aiv");
                var fileId = id + 1;

                switch (lord)
                {
                    case 0: // Rat
                        return !TryLoadAivJsonPacked(basePath, "rat", fileId, out __result);
                    case 1: // Snake
                        return !TryLoadAivJsonPacked(basePath, "snake", fileId, out __result);
                    case 2: // Pig
                        return !TryLoadAivJsonPacked(basePath, "pig", fileId, out __result);
                    case 3: // Wolf
                        return !TryLoadAivJsonPacked(basePath, "wolf", fileId, out __result);
                    case 4: // Saladin
                        return !TryLoadAivJsonPacked(basePath, "saladin", fileId, out __result);
                    case 5: // Caliph
                        return !TryLoadAivJsonPacked(basePath, "caliph", fileId, out __result);
                    case 6: // Sultan
                        return !TryLoadAivJsonPacked(basePath, "sultan", fileId, out __result);
                    case 7: // Richard
                        return !TryLoadAivJsonPacked(basePath, "richard", fileId, out __result);
                    case 8: // Frederick
                        return !TryLoadAivJsonPacked(basePath, "frederick", fileId, out __result);
                    case 9: // Phillip
                        return !TryLoadAivJsonPacked(basePath, "phillip", fileId, out __result);
                    case 10: // Wazir
                        return !TryLoadAivJsonPacked(basePath, "wazir", fileId, out __result);
                    case 11: // Emir
                        return !TryLoadAivJsonPacked(basePath, "emir", fileId, out __result);
                    case 12: // Nizar
                        return !TryLoadAivJsonPacked(basePath, "nizar", fileId, out __result);
                    case 13: // Sheriff
                        return !TryLoadAivJsonPacked(basePath, "sheriff", fileId, out __result);
                    case 14: // Marshal
                        return !TryLoadAivJsonPacked(basePath, "marshal", fileId, out __result);
                    case 15: // Abbot
                        return !TryLoadAivJsonPacked(basePath, "abbot", fileId, out __result);
                    case 16: // Jewel
                        return !TryLoadAivJsonPacked(basePath, "jewel", fileId, out __result);
                    case 17: // Sentinel
                        return !TryLoadAivJsonPacked(basePath, "sentinel", fileId, out __result);
                    case 18: // Nomad
                        return !TryLoadAivJsonPacked(basePath, "nomad", fileId, out __result);
                    case 19: // Kahinah
                        return !TryLoadAivJsonPacked(basePath, "kahinah", fileId, out __result);
                    case 20: // Canary
                        return !TryLoadAivJsonPacked(basePath, "canary", fileId, out __result);
                    case 21: // Trader
                        return !TryLoadAivJsonPacked(basePath, "trader", fileId, out __result);
                    case 22: // Sergeant
                        return !TryLoadAivJsonPacked(basePath, "sergeant", fileId, out __result);
                    case 23: // Lioness
                        return !TryLoadAivJsonPacked(basePath, "lioness", fileId, out __result);
                    case 24: // Croc
                        return !TryLoadAivJsonPacked(basePath, "croc", fileId, out __result);
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("AIVLoader.getAIVData Prefix crashed: " + ex);
                return true; // fall back to original method
            }
        }

        private static bool TryLoadAivJsonPacked(string basePath, string filePrefix, int fileId, out short[] data)
        {
            data = null;

            if (string.IsNullOrEmpty(basePath))
                throw new ArgumentException("basePath is required.", "basePath");
            if (string.IsNullOrEmpty(filePrefix))
                throw new ArgumentException("filePrefix is required.", "filePrefix");

            string aivPath = Path.Combine(basePath, filePrefix + fileId + ".aivjson");

            if (!File.Exists(aivPath))
            {
                MelonLogger.Msg("aivJSON not found: " + aivPath);
                return false;
            }

            MelonLogger.Msg("Parsing aivJSON: " + aivPath);
            data = AivJsonConverter.ConvertAivJsonFileToPackedArray(aivPath);
            return true;
        }
    }
}