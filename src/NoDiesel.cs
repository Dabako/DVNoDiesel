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
        internal static Settings Settings;

        // Livery IDs of modded steam locomotives that count as steam on top of CarTypes.IsSteamLocomotive.
        // Custom Car Loader already reports locomotives whose author set its IsSteamLocomotive flag there.
        internal static HashSet<string> ExtraSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string IdsFile = "steam_ids.txt";

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Mod = modEntry;
            try
            {
                Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
                LoadIds();
                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                RailroadContracts.Patch(harmony);
                modEntry.OnGUI = OnGUI;
                modEntry.OnSaveGUI = e => Settings.Save(e);
                modEntry.OnUpdate = SteamTools.OnUpdate;
            }
            catch (Exception e)
            {
                modEntry.Logger.LogException(e);
                return false;
            }
            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            RailroadContracts.OnGUI();
            SteamTools.OnGUI(modEntry);
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

    public class Settings : UnityModManager.ModSettings
    {
        // Only used when Railroad Contracts is installed.
        public bool RailroadContractsSteamOnly = true;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
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
                SteamTools.GiveOnNextLoad = true;
            }
            catch (Exception e) { Main.Mod?.Logger.LogException(e); }
        }
    }

    // A steam engine can't be run without these: new careers get them, and the UMM settings
    // (Ctrl+F10) have a button for existing saves. Items already in the inventory are skipped.
    internal static class SteamTools
    {
        static readonly string[] Items = { "shovel", "lighter", "Oiler" };
        static string status = "";

        internal static bool GiveOnNextLoad;

        static bool InGame =>
            AStartGameData.carsAndJobsLoadingFinished
            && PlayerManager.PlayerTransform != null
            && DV.Utils.SingletonBehaviour<StartingItemsController>.Instance.itemsLoaded;

        internal static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!GiveOnNextLoad || !InGame) return;
            GiveOnNextLoad = false;
            Main.Log("New career tools: " + Give());
        }

        internal static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (!InGame)
            {
                GUILayout.Label("Load a save to get the steam tools (shovel, lighter, oil can).");
                return;
            }
            if (GUILayout.Button("Give me a shovel, lighter and oil can", GUILayout.Width(320)))
                status = Give();
            if (status.Length > 0) GUILayout.Label(status);
        }

        static string Give()
        {
            var results = new List<string>();
            foreach (var name in Items)
            {
                try
                {
                    var inventory = DV.Utils.SingletonBehaviour<DV.InventorySystem.Inventory>.Instance;
                    var owned = inventory.GetItemsArray(false)
                        .Where(o => o != null)
                        .Select(o => o.GetComponent<InventoryItemSpec>())
                        .FirstOrDefault(s => s != null && s.ItemPrefabName == name);
                    if (owned != null)
                    {
                        // Also repairs items handed out before they were marked as the player's.
                        owned.BelongsToPlayer = true;
                        results.Add(name + ": already have it");
                        continue;
                    }
                    // Dropped or stored somewhere else: the lost and found can bring it back.
                    bool ownedElsewhere = DV.Utils.SingletonBehaviour<StorageController>.Instance.GetAllStorageItems()
                        .Where(i => i != null)
                        .Select(i => i.GetComponent<InventoryItemSpec>())
                        .Any(s => s != null && s.BelongsToPlayer && s.ItemPrefabName == name);
                    if (ownedElsewhere)
                    {
                        results.Add(name + ": you already own one, check the lost and found");
                        continue;
                    }
                    var spec = DV.Globals.G.Items.items.FirstOrDefault(i =>
                        i != null && string.Equals(i.ItemPrefabName, name, StringComparison.OrdinalIgnoreCase));
                    if (spec == null)
                    {
                        results.Add(name + ": not found");
                        continue;
                    }
                    var item = UnityEngine.Object.Instantiate(spec.gameObject);
                    item.name = spec.ItemPrefabName;
                    // Same as a shop purchase: the lost and found only returns the player's items.
                    item.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
                    item.SetActive(true);
                    inventory.AddItemToInventory(item);
                    results.Add(name + ": added");
                }
                catch (Exception e)
                {
                    Main.Mod?.Logger.LogException(e);
                    results.Add(name + ": failed, see log");
                }
            }
            return string.Join(", ", results);
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

        internal static TrainCarLivery GetS060()
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

    // ---------- Railroad Contracts (optional): its starter DE2 and its locomotive shop ----------

    // Railroad Contracts turns off station spawns, gives a new career one owned DE2 and sells
    // locomotives at the kiosk, with their license. Both bypass the patches above, so when it is
    // installed (and the setting is on) its starter becomes an S060 and its shop drops every
    // vehicle that needs a blocked license. Patched by name: its classes are internal, and a
    // version that renames them just leaves this part off.
    internal static class RailroadContracts
    {
        const string ShopType = "Contracts.Mod.Runtime.LocoShop";

        static bool installed;

        static bool Active => installed && Main.Settings.RailroadContractsSteamOnly;

        internal static void Patch(Harmony harmony)
        {
            var shop = AccessTools.TypeByName(ShopType);
            if (shop == null) return; // not installed
            var catalogue = AccessTools.Method(shop, "Catalogue");
            var deliver = AccessTools.Method(shop, "Deliver");
            if (catalogue == null || deliver == null)
            {
                Main.Log("Railroad Contracts found, but not its shop methods: steam-only shop and starter are off");
                return;
            }
            harmony.Patch(catalogue, postfix: new HarmonyMethod(typeof(RailroadContracts), nameof(CataloguePostfix)));
            harmony.Patch(deliver, prefix: new HarmonyMethod(typeof(RailroadContracts), nameof(DeliverPrefix)));
            installed = true;
            Main.Log("Railroad Contracts found: steam-only shop and starter patched");
        }

        internal static void OnGUI()
        {
            if (!installed) return;
            Main.Settings.RailroadContractsSteamOnly = GUILayout.Toggle(Main.Settings.RailroadContractsSteamOnly,
                " Railroad Contracts: S060 as the starter locomotive, no diesels in the kiosk shop");
        }

        // A fresh list on every call: removing from it changes nothing else.
        static void CataloguePostfix(object __result)
        {
            try
            {
                if (!Active || !(__result is System.Collections.IList items)) return;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var cars = Traverse.Create(items[i]).Field("Cars").GetValue<List<TrainCarLivery>>();
                    if (cars != null && cars.Any(c => c?.requiredLicense != null && Main.BlockedLicenseIds.Contains(c.requiredLicense.id)))
                        items.RemoveAt(i);
                }
            }
            catch (Exception e) { Main.Mod?.Logger.LogException(e); }
        }

        // Deliver is only called for the new career's starter.
        static void DeliverPrefix(ref IEnumerable<TrainCarLivery> cars)
        {
            try
            {
                if (!Active || cars == null) return;
                var list = cars.ToList();
                if (list.Count != 1 || list[0]?.v1 != TrainCarType.LocoShunter) return;
                var s060 = StationLocoSpawner_Start_Patch.GetS060();
                if (s060 == null)
                {
                    Main.Log("Railroad Contracts starter: S060 livery not found, keeping the DE2");
                    return;
                }
                cars = new[] { s060 };
                Main.Log("Railroad Contracts starter: S060 instead of the DE2");
            }
            catch (Exception e) { Main.Mod?.Logger.LogException(e); }
        }
    }
}
