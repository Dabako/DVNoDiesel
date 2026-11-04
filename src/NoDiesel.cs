using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace NoDiesel
{
    public static class Main
    {
        internal static UnityModManager.ModEntry Mod;

        // Livery IDs of modded (Custom Car Loader) steam locomotives.
        // The game's CarTypes.IsSteamLocomotive only knows the S282 and S060.
        internal static HashSet<string> ExtraSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string IdsFile = "steam_ids.txt";

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            try
            {
                LoadIds();
                new Harmony(modEntry.Info.Id).PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                modEntry.Logger.LogException(e);
                return false;
            }
            return true;
        }

        static void LoadIds()
        {
            string path = Path.Combine(Mod.Path, IdsFile);
            if (!File.Exists(path))
            {
                File.WriteAllLines(path, new[]
                {
                    "# One livery ID per line: modded steam locomotives that count as steam.",
                    "# The log lists every livery ID a station can spawn.",
                    "Jk3A",
                });
            }
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                ExtraSteamIds.Add(line);
            }
            Log("Extra steam IDs: " + string.Join(", ", ExtraSteamIds));
        }

        // License IDs that can't be bought (diesels never spawn). Resolved lazily: types load after the mod.
        static HashSet<string> blocked;
        const string BlockedFile = "blocked_licenses.txt";

        internal static HashSet<string> BlockedLicenseIds
        {
            get
            {
                if (blocked != null) return blocked;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = Path.Combine(Mod.Path, BlockedFile);
                    if (!File.Exists(path))
                    {
                        File.WriteAllLines(path, new[]
                        {
                            "# General licenses that can't be bought. Enum names (DE2, DE6, DH4, DM3, ...) or license IDs.",
                            "# Licenses you already own are kept.",
                            "DE2", "DE6", "DH4", "DM3",
                        });
                    }
                    foreach (var raw in File.ReadAllLines(path))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        set.Add(line);
                        if (Enum.TryParse(line, true, out GeneralLicenseType t))
                        {
                            var v2 = DV.ThingTypes.TransitionHelpers.TransitionHelpers.ToV2(t);
                            if (v2 != null) set.Add(v2.id);
                        }
                    }
                    Log("Blocked licenses: " + string.Join(", ", set));
                }
                catch (Exception e) { Mod?.Logger.LogException(e); }
                blocked = set;
                return blocked;
            }
        }

        internal static bool IsSteam(TrainCarLivery l) =>
            l != null && (CarTypes.IsSteamLocomotive(l) || ExtraSteamIds.Contains(l.id));

        internal static void Log(string msg) => Mod?.Logger.Log(msg);
    }

    // ---------- Starting licenses: S060 (+ Jk3's S080 if Custom Licenses is installed) instead of DE2 ----------

    internal static class StartLicenses
    {
        internal const string GeneralKey = "Licenses_General";
        internal const string ExtraLicenseId = "S080"; // Jk3, provided by Custom Licenses

        internal static GeneralLicenseType_v2 DE2 => DV.ThingTypes.TransitionHelpers.TransitionHelpers.ToV2(GeneralLicenseType.DE2);
        internal static GeneralLicenseType_v2 S060 => DV.ThingTypes.TransitionHelpers.TransitionHelpers.ToV2(GeneralLicenseType.S060);

        internal static GeneralLicenseType_v2 FindById(string id) =>
            DV.Globals.G.Types.generalLicenses.FirstOrDefault(l => l != null && string.Equals(l.id, id, StringComparison.OrdinalIgnoreCase));

        internal static ICollection<GeneralLicenseType_v2> TutorialSet =>
            (ICollection<GeneralLicenseType_v2>)AccessTools.Field(typeof(LicenseManager), "TutorialGeneralLicenses").GetValue(null);
    }

    // New career (only when the tutorial is skipped; the tutorial itself is built around the DE2).
    [HarmonyPatch(typeof(StartGameData_NewCareer), "PrepareNewSaveData")]
    internal static class NewCareer_Patch
    {
        static void Postfix(ref SaveGameData saveGameData, bool skipTutorial)
        {
            try
            {
                if (!skipTutorial || saveGameData == null) return;
                var list = (saveGameData.GetStringArray(StartLicenses.GeneralKey) ?? new string[0]).ToList();
                var de2 = StartLicenses.DE2;
                list.RemoveAll(id => string.Equals(id, de2.id, StringComparison.OrdinalIgnoreCase));

                var add = new List<GeneralLicenseType_v2> { StartLicenses.S060 };
                var extra = StartLicenses.FindById(StartLicenses.ExtraLicenseId);
                if (extra != null) add.Add(extra);
                else Main.Log("License " + StartLicenses.ExtraLicenseId + " not found (Custom Licenses installed?), skipping it");

                foreach (var l in add)
                {
                    if (!list.Contains(l.id)) list.Add(l.id);
                    UnlockablesManager.Instance.UnlockThing(l);
                }
                saveGameData.SetStringArray(StartLicenses.GeneralKey, list.ToArray());
                Main.Log("New career start licenses: " + string.Join(", ", list));
            }
            catch (Exception e) { Main.Mod?.Logger.LogException(e); }
        }
    }

    // Licenses for locos that never spawn can't be bought. Already acquired ones are kept.
    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsGeneralLicenseObtainable))]
    internal static class BlockLicense_Patch
    {
        static void Postfix(GeneralLicenseType_v2 license, ref bool __result)
        {
            if (__result && license != null && Main.BlockedLicenseIds.Contains(license.id))
                __result = false;
        }
    }

    // Career Manager: show a reason instead of an empty "missing license" screen for blocked licenses.
    [HarmonyPatch(typeof(DV.ServicePenalty.UI.CareerManagerLicensesScreen), "HandleInputAction")]
    internal static class LicenseReason_Patch
    {
        const string Reason =
            "This license is not available.\n\nThe railway runs steam only: diesel locomotives are no longer in service.";

        // private SetInfoData(returnScreen, title, paragraph, info1..4, customAction)
        static readonly MethodInfo SetInfoCustom = typeof(DV.ServicePenalty.UI.CareerManagerInfoScreen)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "SetInfoData" && m.GetParameters().Length == 8);

        static bool Prefix(DV.ServicePenalty.UI.CareerManagerLicensesScreen __instance, DV.ServicePenalty.UI.InputAction input)
        {
            try
            {
                if (input != DV.ServicePenalty.UI.InputAction.Confirm) return true;
                var t = Traverse.Create(__instance);
                int index = t.Field("selector").Property("Current").GetValue<int>();
                var entries = t.Field("licenseEntries").GetValue<System.Collections.IList>();
                if (entries == null || index < 0 || index >= entries.Count) return true;
                var entry = entries[index];
                if (entry == null) return true;
                var e = Traverse.Create(entry); // LicenseEntry is a private nested class
                if (!e.Property("IsGeneralLicenseSet").GetValue<bool>() || e.Property("IsAcquired").GetValue<bool>()) return true;
                var license = e.Property("GeneralLicense").GetValue<GeneralLicenseType_v2>();
                if (license == null || !Main.BlockedLicenseIds.Contains(license.id)) return true;

                var info = t.Field("infoScreen").GetValue<DV.ServicePenalty.UI.CareerManagerInfoScreen>();
                var switcher = t.Field("screenSwitcher").GetValue<DV.ServicePenalty.UI.DisplayScreenSwitcher>();
                if (info == null || switcher == null || SetInfoCustom == null) return true;

                SetInfoCustom.Invoke(info, new object[]
                {
                    __instance, DV.ServicePenalty.UI.CareerManagerLocalization.LICENSES, Reason, "", "", "", "", null
                });
                switcher.SetActiveDisplay(info);
                return false;
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException(e);
                return true;
            }
        }
    }

    // The game re-adds every "tutorial" license (TrainDriver, DE2) missing from a save on load.
    // Vanilla saves always contain DE2, so a save without it was started by this mod: skip DE2 there.
    [HarmonyPatch(typeof(LicenseManager), "LoadData")]
    internal static class LicenseLoad_Patch
    {
        static void Prefix(SaveGameData data, out bool __state)
        {
            __state = false;
            try
            {
                var ids = data?.GetStringArray(StartLicenses.GeneralKey);
                var de2 = StartLicenses.DE2;
                if (ids == null || ids.Contains(de2.id)) return;
                var set = StartLicenses.TutorialSet;
                if (set.Remove(de2))
                {
                    __state = true;
                    Main.Log("Save has no DE2 license: not re-adding it");
                }
            }
            catch (Exception e) { Main.Mod?.Logger.LogException(e); }
        }

        static void Finalizer(bool __state)
        {
            if (!__state) return;
            try { StartLicenses.TutorialSet.Add(StartLicenses.DE2); }
            catch (Exception e) { Main.Mod?.Logger.LogException(e); }
        }
    }

    // Postfix with lowest priority: runs after the game's Start and after Custom Car Loader's
    // Start postfix, which injects modded loco groups. Multiplayer's postfix only starts a
    // coroutine that reads the fields later, so it sees the filtered list too.
    [HarmonyPatch(typeof(StationLocoSpawner), "Start")]
    internal static class StationLocoSpawner_Start_Patch
    {
        static readonly AccessTools.FieldRef<StationLocoSpawner, List<ListTrainCarTypeWrapper>> GroupsRef =
            AccessTools.FieldRefAccess<StationLocoSpawner, List<ListTrainCarTypeWrapper>>("locoTypeGroupsToSpawn");
        static readonly AccessTools.FieldRef<StationLocoSpawner, int> IndexRef =
            AccessTools.FieldRefAccess<StationLocoSpawner, int>("nextLocoGroupSpawnIndex");
        static readonly AccessTools.FieldRef<StationLocoSpawner, string> TrackNameRef =
            AccessTools.FieldRefAccess<StationLocoSpawner, string>("locoSpawnTrackName");

        static TrainCarLivery s060;

        static TrainCarLivery GetS060()
        {
            if (s060 != null) return s060;
            s060 = Resources.FindObjectsOfTypeAll<TrainCarLivery>()
                .Where(l => l != null && l.v1 == TrainCarType.LocoS060)
                .OrderBy(l => l.isHidden ? 1 : 0)
                .FirstOrDefault();
            return s060;
        }

        static string Describe(List<ListTrainCarTypeWrapper> groups) =>
            groups == null ? "null" : string.Join(" / ", groups.Select(g =>
                "[" + (g?.liveries == null ? "?" : string.Join(", ", g.liveries.Select(l => l == null ? "null" : l.id))) + "]"));

        [HarmonyPriority(Priority.Last)]
        static void Postfix(StationLocoSpawner __instance)
        {
            try
            {
                if (__instance == null) return; // game destroyed a broken spawner
                var groups = GroupsRef(__instance);
                if (groups == null || groups.Count == 0) return;

                string where = __instance.gameObject.name + " (" + TrackNameRef(__instance) + ")";
                Main.Log("Station " + where + " original: " + Describe(groups));

                var steam = groups.Where(g => g?.liveries != null && g.liveries.Any(Main.IsSteam)).ToList();

                if (steam.Count == 0)
                {
                    var s = GetS060();
                    if (s == null)
                    {
                        Main.Log("  S060 livery not found, leaving spawner unchanged");
                        return;
                    }
                    steam.Add(new ListTrainCarTypeWrapper(new List<TrainCarLivery> { s }));
                    Main.Log("  no steam group -> forcing " + s.id);
                }

                GroupsRef(__instance) = steam;
                IndexRef(__instance) = UnityEngine.Random.Range(0, steam.Count);
                Main.Log("  now: " + Describe(steam));
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException(e);
            }
        }
    }
}
