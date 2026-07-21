using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

public class CPHInline
{
    // =========================================================
    // EASY EDIT CONFIG
    // =========================================================
    private static readonly HttpClient _http =
        new HttpClient();

    private static readonly object _profileImageCacheLock =
        new object();

    private static readonly Dictionary<string, string>
        _profileImageCache =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );

    private const string FIREBASE_DB_URL =
        "https://new-world-a1aed-default-rtdb.europe-west1.firebasedatabase.app";

    private const int MIN_SECTOR = 1;
    private const int MAX_SECTOR = 10;
    private const int TOTAL_MILKER_RIGS = 3;
    private const int TOTAL_FLAGSHIPS = 4;

    private const string G_MILKER_RIGS =
        "Milker.Rigs";

    private const string G_SECTOR_2_FLAGSHIPS =
        "Main.Sector2.Flagships";

    private const string G_SECTOR_2_FISHING_TAX =
        "Main.Sector2.FishingTax";

    private const string G_GAMBLE_LIMIT =
        "Main.Gamble.Limit";

    private static readonly string[] DEFAULT_SECTOR_NAMES =
    {
        "",
        "Heim's Desert",
        "Ocean Biome",
        "Sector 3",
        "Sector 4",
        "Sector 5",
        "Sector 6",
        "Sector 7",
        "Sector 8",
        "Sector 9",
        "Sector 10"
    };

    private static readonly string[] DEFAULT_BOSS_NAMES =
    {
        "",
        "Immortal Bo",
        "Captain Blackhoof",
        "Colonel Bullseye",
        "Unknown Boss",
        "Unknown Boss",
        "Unknown Boss",
        "Unknown Boss",
        "Unknown Boss",
        "Unknown Boss",
        "Unknown Boss"
    };

    // =========================================================
    // USER VARS
    // =========================================================
    private const string U_CHATTED = "Chatted";
    private const string U_MILKERS = "Main.Milkers";
    private const string U_LEVEL = "Main.Level";
    private const string U_TROOPS = "Main.Troops";
    private const string U_DEPUTY_SECTOR = "Main.SectorDeputy";
    private const string U_CITIZENSHIP_DATE =
        "Main.Citizenship.Date";

    public bool Execute()
    {
        try
        {
            Dictionary<string, int> chattedByUser =
                ReadUserIntValues(U_CHATTED);

            Dictionary<string, long> milkersByUser =
                ReadUserLongValues(U_MILKERS);

            Dictionary<string, int> levelByUser =
                ReadUserIntValues(U_LEVEL);

            Dictionary<string, int> troopsByUser =
                ReadUserIntValues(U_TROOPS);

            Dictionary<string, int> deputySectorByUser =
                ReadUserIntValues(U_DEPUTY_SECTOR);

            Dictionary<string, string> citizenshipDateByUser =
                ReadUserStringValues(U_CITIZENSHIP_DATE);

            Dictionary<int, string> governorBySector =
                LoadGovernors();

            Dictionary<int, List<string>> deputiesBySector =
                LoadDeputies(deputySectorByUser);

            Dictionary<string, object> users =
                new Dictionary<string, object>(
                    StringComparer.OrdinalIgnoreCase
                );

            long totalCitizenTroops = 0L;
            int citizenCount = 0;

            foreach (KeyValuePair<string, int> chatter in chattedByUser)
            {
                if (chatter.Value != 1)
                    continue;

                string login = chatter.Key;

                long milkers = GetLongValue(
                    milkersByUser,
                    login,
                    0L
                );

                int level = Math.Max(
                    1,
                    GetIntValue(
                        levelByUser,
                        login,
                        1
                    )
                );

                int troops = Math.Max(
                    0,
                    GetIntValue(
                        troopsByUser,
                        login,
                        0
                    )
                );

                int deputySector = GetIntValue(
                    deputySectorByUser,
                    login,
                    0
                );

                if (deputySector < MIN_SECTOR ||
                    deputySector > MAX_SECTOR)
                {
                    deputySector = 0;
                }

                List<int> governorSectors =
                    GetGovernorSectors(
                        login,
                        governorBySector
                    );

                users[login] = new UserSnapshot
                {
                    milkers = milkers,
                    level = level,
                    troops = troops,
                    profileImageUrl =
                        GetTwitchProfileImageUrl(login),
                    governorSectors = governorSectors,
                    deputySector = deputySector,
                    citizenshipDate = GetStringValue(
                        citizenshipDateByUser,
                        login,
                        ""
                    )
                };

                totalCitizenTroops = SafeAdd(
                    totalCitizenTroops,
                    troops
                );

                citizenCount++;
            }

            Dictionary<string, SectorSnapshot> sectors =
                new Dictionary<string, SectorSnapshot>();

            long totalGarrisonedTroops = 0L;

            for (int sector = MIN_SECTOR;
                sector <= MAX_SECTOR;
                sector++)
            {
                SectorSnapshot snapshot = BuildSectorSnapshot(
                    sector,
                    governorBySector,
                    deputiesBySector
                );

                sectors[sector.ToString()] = snapshot;

                totalGarrisonedTroops = SafeAdd(
                    totalGarrisonedTroops,
                    snapshot.garrisonedTroops
                );
            }

            SiteSnapshot site = new SiteSnapshot
            {
                lastUpdatedUtc =
                    DateTime.UtcNow.ToString("o"),
                citizenCount = citizenCount,
                totalCitizenTroops =
                    totalCitizenTroops,
                totalGarrisonedTroops =
                    totalGarrisonedTroops,
                sectors = sectors,

                milkerRigsRemaining =
                    sectors["1"].objectiveRemaining,
                milkerRigsTotal =
                    sectors["1"].objectiveTotal,
                flagshipsRemaining =
                    sectors["2"].objectiveRemaining,
                flagshipsTotal =
                    sectors["2"].objectiveTotal,
                fishingTax =
                    GetGlobalInt(
                        G_SECTOR_2_FISHING_TAX,
                        0
                    ) == 1 ? 1 : 0,
                gambleLimit =
                    Math.Max(
                        0,
                        GetGlobalInt(
                            G_GAMBLE_LIMIT,
                            10
                        )
                    ),

                // Kept so the existing Sector 1 website code
                // continues to work during the HTML upgrade.
                sector1 = sectors["1"]
            };

            users["__site"] = site;

            if (!PutFirebase(
                "users",
                users,
                "Site sync"))
            {
                return false;
            }

            CPH.LogInfo(BuildLogSummary(
                citizenCount,
                sectors
            ));

            return true;
        }
        catch (Exception exception)
        {
            CPH.LogError(
                "Site sync error: " +
                exception.Message
            );

            return false;
        }
    }

    // =========================================================
    // SECTOR SNAPSHOTS
    // =========================================================
    private SectorSnapshot BuildSectorSnapshot(
        int sector,
        Dictionary<int, string> governorBySector,
        Dictionary<int, List<string>> deputiesBySector)
    {
        string prefix =
            "Main.Sector" + sector;

        string sectorName = GetGlobalString(
            prefix + ".Name",
            DEFAULT_SECTOR_NAMES[sector]
        );

        if (string.IsNullOrWhiteSpace(sectorName))
            sectorName = DEFAULT_SECTOR_NAMES[sector];

        string bossName = GetGlobalString(
            prefix + ".Boss.Name",
            DEFAULT_BOSS_NAMES[sector]
        );

        if (string.IsNullOrWhiteSpace(bossName))
            bossName = DEFAULT_BOSS_NAMES[sector];

        int bossAlive = GetGlobalEnabled(
            prefix + ".Boss",
            true
        ) ? 1 : 0;

        int bossDefeated =
            bossAlive == 1 ? 0 : 1;

        int enemyControl = ClampControl(
            GetGlobalInt(
                prefix + ".EnemyControl",
                100
            )
        );

        int liberatedControl = ClampControl(
            GetGlobalInt(
                prefix + ".LiberatedControl",
                0
            )
        );

        int garrisonedTroops = Math.Max(
            0,
            GetGlobalInt(
                prefix + ".Garrison",
                0
            )
        );

        int krakenReleased =
            sector == 2 &&
            GetGlobalInt(
                prefix + ".Kraken",
                0
            ) == 1
                ? 1
                : 0;

        int intel = sector == 3
            ? Math.Max(
                0,
                GetGlobalInt(prefix + ".Intel", 0)
            )
            : 0;

        int bossStage = sector == 3
            ? Math.Max(
                0,
                GetGlobalInt(prefix + ".Boss.Stage", 0)
            )
            : 0;

        int miicbmFunds = sector == 3
            ? Math.Max(
                0,
                GetGlobalInt("MIICBM.FUNDS", 0)
            )
            : 0;

        int hitsquadEnabled =
            sector == 3 &&
            GetGlobalEnabled(
                prefix + ".Hitsquad.Enabled"
            )
                ? 1
                : 0;

        string governor = governorBySector.ContainsKey(sector)
            ? governorBySector[sector]
            : "";

        if (sector == 1 && bossAlive == 0)
            governor = "Heimidinger";

        string governorProfileImageUrl =
            sector == 1
                ? ""
                : GetTwitchProfileImageUrl(governor);

        List<string> deputies =
            deputiesBySector.ContainsKey(sector)
                ? CopyStrings(deputiesBySector[sector])
                : new List<string>();

        int displayedControl = bossAlive == 1
            ? enemyControl
            : liberatedControl;

        string controlOwner = bossAlive == 1
            ? "enemy"
            : "liberated";

        string controlLabel = bossAlive == 1
            ? "Enemy Control"
            : "Liberated Control";

        string displayedLeader;
        string displayedLeaderRole;
        string objectiveName = "";
        int objectiveRemaining = 0;
        int objectiveTotal = 0;

        if (bossAlive == 1)
        {
            displayedLeader = bossName;
            displayedLeaderRole = "Boss";
        }
        else if (sector == 1)
        {
            displayedLeader = "Heimidinger";
            displayedLeaderRole = "Claimed By";
        }
        else if (!string.IsNullOrWhiteSpace(governor))
        {
            displayedLeader = governor;
            displayedLeaderRole = "Governor";
        }
        else
        {
            displayedLeader = "Unclaimed";
            displayedLeaderRole = "Unclaimed";
        }

        if (sector == 1)
        {
            objectiveName = "Milker Rigs";
            objectiveTotal = TOTAL_MILKER_RIGS;
            objectiveRemaining = ClampCount(
                GetGlobalInt(
                    G_MILKER_RIGS,
                    TOTAL_MILKER_RIGS
                ),
                objectiveTotal
            );
        }
        else if (sector == 2)
        {
            objectiveName = "Flagships";
            objectiveTotal = TOTAL_FLAGSHIPS;
            objectiveRemaining = ClampCount(
                GetGlobalInt(
                    G_SECTOR_2_FLAGSHIPS,
                    TOTAL_FLAGSHIPS
                ),
                objectiveTotal
            );
        }

        return new SectorSnapshot
        {
            sector = sector,
            sectorName = sectorName,

            bossName = bossName,
            bossAlive = bossAlive,
            bossDefeated = bossDefeated,

            enemyControl = enemyControl,
            liberatedControl = liberatedControl,

            // "control" is the correct value for the
            // website to display for the current owner.
            control = displayedControl,
            displayedControl = displayedControl,
            controlOwner = controlOwner,
            controlLabel = controlLabel,

            displayedLeader = displayedLeader,
            displayedLeaderRole =
                displayedLeaderRole,

            governor = governor,
            governorProfileImageUrl =
                governorProfileImageUrl,
            deputies = deputies,
            garrisonedTroops =
                garrisonedTroops,
            krakenReleased =
                krakenReleased,
            intel = intel,
            bossStage = bossStage,
            miicbmFunds = miicbmFunds,
            hitsquadEnabled =
                hitsquadEnabled,

            objectiveName = objectiveName,
            objectiveRemaining = objectiveRemaining,
            objectiveTotal = objectiveTotal
        };
    }

    private string GetTwitchProfileImageUrl(
        string userLogin)
    {
        if (string.IsNullOrWhiteSpace(userLogin))
            return "";

        string normalizedLogin = userLogin.Trim();

        lock (_profileImageCacheLock)
        {
            string cachedUrl;
            if (_profileImageCache.TryGetValue(
                normalizedLogin,
                out cachedUrl))
            {
                return cachedUrl;
            }
        }

        try
        {
            var userInfo =
                CPH.TwitchGetExtendedUserInfoByLogin(
                    normalizedLogin
                );

            string profileImageUrl = userInfo == null
                ? ""
                : userInfo.ProfileImageUrl ?? "";

            if (!string.IsNullOrWhiteSpace(
                profileImageUrl))
            {
                lock (_profileImageCacheLock)
                {
                    _profileImageCache[normalizedLogin] =
                        profileImageUrl;
                }
            }

            return profileImageUrl;
        }
        catch (Exception ex)
        {
            CPH.LogWarn(
                "Profile image lookup failed for " +
                normalizedLogin +
                ": " +
                ex.Message
            );
            return "";
        }
    }

    private Dictionary<int, string> LoadGovernors()
    {
        Dictionary<int, string> result =
            new Dictionary<int, string>();

        for (int sector = MIN_SECTOR;
            sector <= MAX_SECTOR;
            sector++)
        {
            string variable =
                "Main.Sector" +
                sector +
                ".Gov";

            string governor =
                FindEnabledTwitchUser(variable);

            if (!string.IsNullOrWhiteSpace(governor))
            {
                result[sector] = governor;
            }

            if (sector == 3)
            {
                CPH.LogInfo(
                    "Sector 3 governor lookup | " +
                    variable +
                    " = " +
                    (
                        string.IsNullOrWhiteSpace(governor)
                            ? "<none>"
                            : governor
                    )
                );
            }
        }

        return result;
    }

    private string FindEnabledTwitchUser(
        string variable)
    {
        Dictionary<string, bool> enabledUsers =
            new Dictionary<string, bool>(
                StringComparer.OrdinalIgnoreCase
            );

        Dictionary<string, int> intValues =
            ReadUserIntValues(variable);

        foreach (KeyValuePair<string, int> entry
            in intValues)
        {
            if (entry.Value != 0)
                enabledUsers[entry.Key] = true;
        }

        if (enabledUsers.Count == 0)
        {
            Dictionary<string, long> longValues =
                ReadUserLongValues(variable);

            foreach (KeyValuePair<string, long> entry
                in longValues)
            {
                if (entry.Value != 0L)
                    enabledUsers[entry.Key] = true;
            }
        }

        if (enabledUsers.Count == 0)
        {
            Dictionary<string, bool> boolValues =
                ReadUserBoolValues(variable);

            foreach (KeyValuePair<string, bool> entry
                in boolValues)
            {
                if (entry.Value)
                    enabledUsers[entry.Key] = true;
            }
        }

        if (enabledUsers.Count == 0)
        {
            Dictionary<string, string> stringValues =
                ReadUserStringValues(variable);

            foreach (KeyValuePair<string, string> entry
                in stringValues)
            {
                string value = entry.Value.Trim();

                if (value == "1" ||
                    value.Equals(
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    value.Equals(
                        "active",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    value.Equals(
                        "enabled",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    enabledUsers[entry.Key] = true;
                }
            }
        }

        if (enabledUsers.Count == 0)
            return "";

        List<string> candidates =
            new List<string>(enabledUsers.Keys);

        candidates.Sort(
            StringComparer.OrdinalIgnoreCase
        );

        if (candidates.Count > 1)
        {
            CPH.LogWarn(
                variable +
                " has multiple enabled users. Using " +
                candidates[0] +
                "."
            );
        }

        return candidates[0];
    }

    private Dictionary<int, List<string>> LoadDeputies(
        Dictionary<string, int> deputySectorByUser)
    {
        Dictionary<int, List<string>> result =
            new Dictionary<int, List<string>>();

        for (int sector = MIN_SECTOR;
            sector <= MAX_SECTOR;
            sector++)
        {
            result[sector] = new List<string>();
        }

        foreach (KeyValuePair<string, int> entry
            in deputySectorByUser)
        {
            int sector = entry.Value;

            if (sector < MIN_SECTOR ||
                sector > MAX_SECTOR)
            {
                continue;
            }

            result[sector].Add(entry.Key);
        }

        for (int sector = MIN_SECTOR;
            sector <= MAX_SECTOR;
            sector++)
        {
            result[sector].Sort(
                StringComparer.OrdinalIgnoreCase
            );
        }

        return result;
    }

    private List<int> GetGovernorSectors(
        string user,
        Dictionary<int, string> governorBySector)
    {
        List<int> result = new List<int>();

        for (int sector = MIN_SECTOR;
            sector <= MAX_SECTOR;
            sector++)
        {
            if (!governorBySector.ContainsKey(sector))
                continue;

            if (governorBySector[sector].Equals(
                user,
                StringComparison.OrdinalIgnoreCase))
            {
                result.Add(sector);
            }
        }

        return result;
    }

    // =========================================================
    // USER VARIABLE READERS
    // =========================================================
    private Dictionary<string, int> ReadUserIntValues(
        string variable)
    {
        Dictionary<string, int> result =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase
            );

        try
        {
            var values = CPH.GetTwitchUsersVar<int>(
                variable,
                true
            );

            if (values == null)
                return result;

            foreach (var entry in values)
            {
                string login = NormalizeLogin(
                    entry.UserLogin
                );

                if (!string.IsNullOrWhiteSpace(login))
                    result[login] = entry.Value;
            }
        }
        catch { }

        return result;
    }

    private Dictionary<string, long> ReadUserLongValues(
        string variable)
    {
        Dictionary<string, long> result =
            new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase
            );

        try
        {
            var values = CPH.GetTwitchUsersVar<long>(
                variable,
                true
            );

            if (values == null)
                return result;

            foreach (var entry in values)
            {
                string login = NormalizeLogin(
                    entry.UserLogin
                );

                if (!string.IsNullOrWhiteSpace(login))
                    result[login] = entry.Value;
            }
        }
        catch { }

        return result;
    }

    private Dictionary<string, bool> ReadUserBoolValues(
        string variable)
    {
        Dictionary<string, bool> result =
            new Dictionary<string, bool>(
                StringComparer.OrdinalIgnoreCase
            );

        try
        {
            var values = CPH.GetTwitchUsersVar<bool>(
                variable,
                true
            );

            if (values == null)
                return result;

            foreach (var entry in values)
            {
                string login = NormalizeLogin(
                    entry.UserLogin
                );

                if (!string.IsNullOrWhiteSpace(login))
                    result[login] = entry.Value;
            }
        }
        catch { }

        return result;
    }

    private Dictionary<string, string> ReadUserStringValues(
        string variable)
    {
        Dictionary<string, string> result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );

        try
        {
            var values = CPH.GetTwitchUsersVar<string>(
                variable,
                true
            );

            if (values == null)
                return result;

            foreach (var entry in values)
            {
                string login = NormalizeLogin(
                    entry.UserLogin
                );

                string value = entry.Value == null
                    ? ""
                    : entry.Value.Trim();

                if (!string.IsNullOrWhiteSpace(login) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    result[login] = value;
                }
            }
        }
        catch { }

        return result;
    }

    // =========================================================
    // FIREBASE
    // =========================================================
    private bool PutFirebase(
        string path,
        object value,
        string label)
    {
        string json =
            JsonConvert.SerializeObject(value);

        using (var payload = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"))
        {
            HttpResponseMessage response = _http
                .PutAsync(
                    FIREBASE_DB_URL +
                    "/" +
                    path +
                    ".json",
                    payload
                )
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode)
                return true;

            CPH.LogError(
                label +
                " failed: " +
                response.StatusCode +
                " - " +
                response.ReasonPhrase
            );

            return false;
        }
    }

    // =========================================================
    // HELPERS
    // =========================================================
    private string BuildLogSummary(
        int citizenCount,
        Dictionary<string, SectorSnapshot> sectors)
    {
        StringBuilder text = new StringBuilder();

        text.Append("Site sync OK - ");
        text.Append(citizenCount);
        text.Append(" citizens");

        for (int sector = MIN_SECTOR;
            sector <= MAX_SECTOR;
            sector++)
        {
            SectorSnapshot snapshot =
                sectors[sector.ToString()];

            text.Append(" | S");
            text.Append(sector);
            text.Append(" ");
            text.Append(snapshot.controlOwner);
            text.Append(" ");
            text.Append(snapshot.displayedControl);
            text.Append("%");
        }

        text.Append(" | S3 Hitsquad ");
        text.Append(
            sectors["3"].hitsquadEnabled == 1
                ? "ACTIVE"
                : "OFF"
        );

        return text.ToString();
    }

    private string NormalizeLogin(string login)
    {
        return string.IsNullOrWhiteSpace(login)
            ? ""
            : login.Trim().ToLower();
    }

    private int GetIntValue(
        Dictionary<string, int> values,
        string user,
        int fallback)
    {
        return values.ContainsKey(user)
            ? values[user]
            : fallback;
    }

    private long GetLongValue(
        Dictionary<string, long> values,
        string user,
        long fallback)
    {
        return values.ContainsKey(user)
            ? values[user]
            : fallback;
    }

    private string GetStringValue(
        Dictionary<string, string> values,
        string user,
        string fallback)
    {
        return values.ContainsKey(user) &&
            !string.IsNullOrWhiteSpace(values[user])
                ? values[user]
                : fallback;
    }

    private List<string> CopyStrings(
        List<string> source)
    {
        List<string> result =
            new List<string>();

        for (int i = 0; i < source.Count; i++)
            result.Add(source[i]);

        return result;
    }

    private int ClampControl(int value)
    {
        if (value < 0)
            return 0;

        if (value > 100)
            return 100;

        return value;
    }

    private int ClampCount(
        int value,
        int maximum)
    {
        if (value < 0)
            return 0;

        if (value > maximum)
            return maximum;

        return value;
    }

    private long SafeAdd(long a, long b)
    {
        if (b > 0L &&
            a > long.MaxValue - b)
        {
            return long.MaxValue;
        }

        if (b < 0L &&
            a < long.MinValue - b)
        {
            return long.MinValue;
        }

        return a + b;
    }

    private int GetGlobalInt(
        string variable,
        int fallback)
    {
        try
        {
            return CPH.GetGlobalVar<int>(
                variable,
                true
            );
        }
        catch { return fallback; }
    }

    private bool GetGlobalEnabled(
        string variable,
        bool fallback = false)
    {
        bool[] scopes = { true, false };

        for (int i = 0; i < scopes.Length; i++)
        {
            bool persisted = scopes[i];

            try
            {
                int? number =
                    CPH.GetGlobalVar<int?>(
                        variable,
                        persisted
                    );

                if (number.HasValue)
                    return number.Value != 0;
            }
            catch { }

            try
            {
                bool? enabled =
                    CPH.GetGlobalVar<bool?>(
                        variable,
                        persisted
                    );

                if (enabled.HasValue)
                    return enabled.Value;
            }
            catch { }

            try
            {
                string text =
                    CPH.GetGlobalVar<string>(
                        variable,
                        persisted
                    );

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                text = text.Trim();

                if (text == "1" ||
                    text.Equals(
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    text.Equals(
                        "active",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    text.Equals(
                        "enabled",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return true;
                }

                if (text == "0" ||
                    text.Equals(
                        "false",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    text.Equals(
                        "off",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    text.Equals(
                        "disabled",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return false;
                }
            }
            catch { }
        }

        return fallback;
    }

    private string GetGlobalString(
        string variable,
        string fallback)
    {
        try
        {
            string value =
                CPH.GetGlobalVar<string>(
                    variable,
                    true
                );

            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value;
        }
        catch { return fallback; }
    }

    // =========================================================
    // FIREBASE DATA SHAPES
    // =========================================================
    private class UserSnapshot
    {
        public long milkers { get; set; }
        public int level { get; set; }
        public int troops { get; set; }
        public string profileImageUrl { get; set; }
        public List<int> governorSectors { get; set; }
        public int deputySector { get; set; }
        public string citizenshipDate { get; set; }
    }

    private class SectorSnapshot
    {
        public int sector { get; set; }
        public string sectorName { get; set; }

        public string bossName { get; set; }
        public int bossAlive { get; set; }
        public int bossDefeated { get; set; }

        public int enemyControl { get; set; }
        public int liberatedControl { get; set; }

        public int control { get; set; }
        public int displayedControl { get; set; }
        public string controlOwner { get; set; }
        public string controlLabel { get; set; }

        public string displayedLeader { get; set; }
        public string displayedLeaderRole { get; set; }

        public string governor { get; set; }
        public string governorProfileImageUrl { get; set; }
        public List<string> deputies { get; set; }
        public int garrisonedTroops { get; set; }
        public int krakenReleased { get; set; }
        public int intel { get; set; }
        public int bossStage { get; set; }
        public int miicbmFunds { get; set; }
        public int hitsquadEnabled { get; set; }

        public string objectiveName { get; set; }
        public int objectiveRemaining { get; set; }
        public int objectiveTotal { get; set; }
    }

    private class SiteSnapshot
    {
        public string lastUpdatedUtc { get; set; }
        public int citizenCount { get; set; }
        public long totalCitizenTroops { get; set; }
        public long totalGarrisonedTroops { get; set; }

        public int milkerRigsRemaining { get; set; }
        public int milkerRigsTotal { get; set; }
        public int flagshipsRemaining { get; set; }
        public int flagshipsTotal { get; set; }
        public int fishingTax { get; set; }
        public int gambleLimit { get; set; }

        public SectorSnapshot sector1 { get; set; }

        public Dictionary<string, SectorSnapshot> sectors
        {
            get;
            set;
        }
    }
}
