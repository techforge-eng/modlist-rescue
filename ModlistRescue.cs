// Tech-Forge's Modlist Rescue - Skyrim Special Edition (SteamCMD edition)
// Re-installs a chosen Skyrim SE game version so a Steam update no longer breaks a modlist.
//   1. Makes sure Steam is open and the signed-in account owns Skyrim SE
//   2. Lets you pick the version to install (1.5.97, 1.6.640 or 1.6.1170)
//   3. Downloads that version's 3 depots with SteamCMD (Valve's command-line Steam)
//   4. Copies them into the Skyrim SE folder, overwriting automatically
//   5. Writes a full run log into the Skyrim folder
// Builds with the C# compiler included in Windows (.NET Framework 4.x) - see build.bat.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Tech-Forge's Modlist Rescue - Skyrim SE")]
[assembly: System.Reflection.AssemblyProduct("Tech-Forge's Modlist Rescue")]
[assembly: System.Reflection.AssemblyCompany("Tech-Forge")]
[assembly: System.Reflection.AssemblyCopyright("Tech-Forge")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace ModlistRescue
{
    internal static class Program
    {
        const string AppId = "489830";
        const string SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        const ulong SteamId64Base = 76561197960265728UL;

        // Selectable Skyrim SE game versions, each with its three depot manifest IDs.
        sealed class GameVersion
        {
            public readonly string Name;   // e.g. "1.6.640"
            public readonly string Blurb;  // short description shown in the menu
            public readonly string[][] Depots; // { depotId, manifestId }
            public GameVersion(string name, string blurb, string[][] depots) { Name = name; Blurb = blurb; Depots = depots; }
        }

        static readonly GameVersion[] Versions = new GameVersion[]
        {
            new GameVersion("1.5.97",
                "Special Edition - the classic pre-Anniversary build. The most widely supported modding target; most older guides, SKSE builds and mods assume this version.",
                new string[][]
                {
                    new string[] { "489831", "7848722008564294070" },
                    new string[] { "489832", "8702665189575304780" },
                    new string[] { "489833", "2289561010626853674" },
                }),
            new GameVersion("1.6.640",
                "Anniversary Edition - a long-stable AE build and a common target for AE modlists that were never updated for the newest patches.",
                new string[][]
                {
                    new string[] { "489831", "3660787314279169352" },
                    new string[] { "489832", "2756691988703496654" },
                    new string[] { "489833", "5291801952219815735" },
                }),
            new GameVersion("1.6.1170",
                "Anniversary Edition - a recent build. Pick this if your modlist specifically targets current AE.",
                new string[][]
                {
                    new string[] { "489831", "8442952117333549665" },
                    new string[] { "489832", "8042843504692938467" },
                    new string[] { "489833", "1914580699073641964" },
                }),
        };

        static GameVersion chosen;

        // Files left behind by Steam emulators / cracks
        static readonly string[] CrackMarkers = new string[]
        {
            "steam_emu.ini", "SmartSteamEmu.ini", "CODEX.ini", "CPY.ini", "SKIDROW.ini", "3DMGAME.ini",
            "ALI213.ini", "cream_api.ini", "steam_interfaces.txt", "valve.ini", "steam_settings"
        };

        static string exeDir;
        static string steamPath;
        static string gamePath;
        static string acfPath;
        static string steamCmdDir;
        static string contentRoot;

        static int Main(string[] args)
        {
            Theme.Init();
            bool useExisting = args.Any(a => a.Equals("--use-existing", StringComparison.OrdinalIgnoreCase));
            string versionArg = null;
            for (int i = 0; i < args.Length; i++)
                if (args[i].Equals("--version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) versionArg = args[i + 1];
            int exitCode = 0;

            try
            {
                exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                steamCmdDir = Path.Combine(exeDir, "steamcmd");
                contentRoot = Path.Combine(steamCmdDir, "steamapps", "content", "app_" + AppId);

                Banner();
                Log.Write("Tech-Forge's Modlist Rescue started.");

                Step("Locating Steam...");
                steamPath = FindSteam();
                Ok("Steam found: " + steamPath);

                Step("Locating Skyrim Special Edition...");
                FindSkyrim(steamPath);
                Ok("Skyrim SE found: " + gamePath);
                Log.Open(gamePath);
                Info("Writing a log to: " + Log.FilePath);
                PrintVersion("Current SkyrimSE.exe version");

                if (versionArg != null) chosen = FindVersionByName(versionArg);
                if (chosen == null && !useExisting) chosen = ChooseVersion();
                if (chosen == null) chosen = Versions[0];
                Ok("Selected version: v" + chosen.Name);

                if (useExisting)
                {
                    Step("Using previously downloaded depots (--use-existing).");
                }
                else
                {
                    Console.WriteLine();
                    ulong steamId = EnsureSteamOpenAndLoggedIn();
                    string accountName = VerifyOwnership(steamId);

                    Console.WriteLine();
                    if (!AskYesNo("Everything checks out. Download Skyrim v" + chosen.Name + " and patch now?", true))
                    {
                        Info("Cancelled - nothing was changed.");
                        return Finish(0);
                    }

                    WaitForSkyrimClosed();
                    EnsureSteamCmd();
                    DownloadWithSteamCmd(accountName);
                }

                WaitForSkyrimClosed();
                CopyDepots();
                PrintVersion("New SkyrimSE.exe version");

                Console.WriteLine();
                Ok("QUEST COMPLETE: Skyrim v" + chosen.Name + " is installed. Sovngarde awaits, Dragonborn.");
                Info("Tip: in Steam > Skyrim SE > Properties > Updates, set it to only update when you launch it,");
                Info("     then start the game through SKSE / your mod manager so Steam doesn't re-update it.");
                Info("     (Steam 'Verify integrity of game files' will put the latest version back.)");
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!IsAdmin())
                {
                    Warn("Windows blocked writing to the game folder: " + ex.Message);
                    Warn("Relaunching as Administrator to finish copying (downloads will be reused)...");
                    if (RelaunchElevated()) return 2;
                    Err("Could not relaunch as Administrator. Right-click the exe > Run as administrator.");
                }
                else Err(ex.Message);
                exitCode = 1;
            }
            catch (Exception ex)
            {
                Err(ex.Message);
                exitCode = 1;
            }

            return Finish(exitCode);
        }

        static int Finish(int code)
        {
            Log.Write("Finished with exit code " + code + ".");
            Console.WriteLine();
            if (Log.FilePath != null) Info("Log saved to: " + Log.FilePath);
            Theme.Use(Theme.Text);
            Console.WriteLine("Press any key to close...");
            try { Console.ReadKey(true); } catch { Thread.Sleep(5000); }
            Theme.Reset();
            return code;
        }

        // ------------------------------------------------------------------ discovery

        static string FindSteam()
        {
            string p = RegString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (!ValidSteam(p)) p = RegString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            if (!ValidSteam(p)) p = RegString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            if (!ValidSteam(p)) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            if (!ValidSteam(p)) throw new Exception("Could not find your Steam installation. Is Steam installed?");
            return Path.GetFullPath(p.Replace('/', '\\'));
        }

        static bool ValidSteam(string p)
        {
            return !string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p.Replace('/', '\\'), "steam.exe"));
        }

        static string RegString(RegistryKey root, string sub, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(sub))
                {
                    if (k == null) return null;
                    return k.GetValue(name) as string;
                }
            }
            catch { return null; }
        }

        static void FindSkyrim(string steam)
        {
            List<string> libs = new List<string>();
            libs.Add(steam);

            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                string text = File.ReadAllText(vdf);
                List<string> found = new List<string>();
                foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    found.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(text, "\"\\d+\"\\s+\"([A-Za-z]:\\\\[^\"]*)\""))
                    found.Add(m.Groups[1].Value);

                foreach (string raw in found)
                {
                    string lp = raw.Replace("\\\\", "\\");
                    if (!libs.Any(x => string.Equals(x.TrimEnd('\\'), lp.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                        libs.Add(lp);
                }
            }

            foreach (string lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps");
                string acf = Path.Combine(apps, "appmanifest_" + AppId + ".acf");
                if (!File.Exists(acf)) continue;

                string installDir = AcfValue(File.ReadAllText(acf), "installdir");
                if (string.IsNullOrEmpty(installDir)) installDir = "Skyrim Special Edition";
                string game = Path.Combine(apps, "common", installDir);
                if (File.Exists(Path.Combine(game, "SkyrimSE.exe")))
                {
                    gamePath = game;
                    acfPath = acf;
                    return;
                }
            }

            throw new Exception("Could not find a Steam install of Skyrim Special Edition. Install it through Steam first.");
        }

        static string AcfValue(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        // ------------------------------------------------------------------ steam app + ownership

        static bool SteamRunning()
        {
            return Process.GetProcessesByName("steam").Length > 0;
        }

        // Steam writes the logged-in account ID here while it's running (0 = not logged in)
        static uint ActiveUser()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    if (k == null) return 0;
                    object v = k.GetValue("ActiveUser");
                    if (v == null) return 0;
                    return unchecked((uint)Convert.ToInt32(v));
                }
            }
            catch { return 0; }
        }

        static ulong EnsureSteamOpenAndLoggedIn()
        {
            Step("Checking Steam...");
            if (!SteamRunning())
            {
                Warn("Steam isn't open.");
                if (!AskYesNo("Open Steam now?", true))
                    throw new Exception("Steam needs to be open and logged in to check you own Skyrim.");
                Process.Start(Path.Combine(steamPath, "steam.exe"));
                Info("Starting Steam...");
            }

            if (ActiveUser() == 0)
            {
                Info("Waiting for you to log in to Steam (up to 5 minutes)...");
                DateTime until = DateTime.Now.AddMinutes(5);
                while (ActiveUser() == 0)
                {
                    if (DateTime.Now > until) throw new Exception("Timed out waiting for a Steam login.");
                    Thread.Sleep(1000);
                }
            }

            ulong steamId = SteamId64Base + ActiveUser();
            Ok("Steam is open and logged in.");
            return steamId;
        }

        // Returns the account name to use for SteamCMD
        static string VerifyOwnership(ulong steamId)
        {
            Step("Checking Skyrim Special Edition is legitimately owned...");

            string accountName, personaName;
            ReadLoginUser(steamId, out accountName, out personaName);
            Info("Logged in as: " + (personaName ?? "?") + (accountName != null ? " (" + accountName + ")" : ""));

            // 1. Installed through Steam
            Ok("Skyrim SE is installed through Steam (" + Path.GetFileName(acfPath) + ").");

            // 2. Install belongs to the logged-in account
            string acf = File.ReadAllText(acfPath);
            string lastOwner = AcfValue(acf, "LastOwner");
            ulong owner;
            if (!string.IsNullOrEmpty(lastOwner) && ulong.TryParse(lastOwner, out owner) && owner != 0)
            {
                if (owner != steamId)
                    throw new Exception("Skyrim SE on this PC belongs to a different Steam account (or is Family Shared).\n" +
                                        "  Log in to Steam with the account that bought Skyrim SE and run this again.");
                Ok("The logged-in account owns this copy of Skyrim SE.");
            }
            else
            {
                Warn("Couldn't read the owner from Steam's game manifest - Valve will still check ownership during download.");
            }

            // 3. No Steam emulator / crack files in the game folder
            List<string> hits = new List<string>();
            foreach (string marker in CrackMarkers)
            {
                string p = Path.Combine(gamePath, marker);
                if (File.Exists(p) || Directory.Exists(p)) hits.Add(marker);
            }
            if (hits.Count > 0)
                throw new Exception("Found files from a Steam emulator / crack in the game folder: " + string.Join(", ", hits.ToArray()) +
                                    "\n  This tool only works with a legitimate Steam copy.");
            Ok("No Steam emulator / crack files found.");

            Info("(Valve's servers also refuse depot downloads for games the account doesn't own.)");
            return accountName;
        }

        static void ReadLoginUser(ulong steamId, out string accountName, out string personaName)
        {
            accountName = null;
            personaName = null;
            try
            {
                string file = Path.Combine(steamPath, "config", "loginusers.vdf");
                if (!File.Exists(file)) return;
                string text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text, "\"(\\d{17})\"\\s*\\{([^}]*)\\}"))
                {
                    if (m.Groups[1].Value != steamId.ToString()) continue;
                    accountName = AcfValue(m.Groups[2].Value, "AccountName");
                    personaName = AcfValue(m.Groups[2].Value, "PersonaName");
                    return;
                }
            }
            catch { }
        }

        static void WaitForSkyrimClosed()
        {
            bool warned = false;
            while (Process.GetProcessesByName("SkyrimSE").Length > 0 ||
                   Process.GetProcessesByName("SkyrimSELauncher").Length > 0)
            {
                if (!warned) { Warn("Skyrim is running - please close it. Waiting..."); warned = true; }
                Thread.Sleep(2000);
            }
        }

        // ------------------------------------------------------------------ steamcmd

        static void EnsureSteamCmd()
        {
            string exe = Path.Combine(steamCmdDir, "steamcmd.exe");
            if (File.Exists(exe))
            {
                Ok("SteamCMD found: " + exe);
                return;
            }

            Step("Downloading SteamCMD from Valve...");
            Directory.CreateDirectory(steamCmdDir);
            string zip = Path.Combine(steamCmdDir, "steamcmd.zip");
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
            using (WebClient wc = new WebClient())
                wc.DownloadFile(SteamCmdUrl, zip);

            Step("Extracting SteamCMD...");
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string dest = Path.GetFullPath(Path.Combine(steamCmdDir, entry.FullName));
                    if (!dest.StartsWith(steamCmdDir, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            }
            File.Delete(zip);
            if (!File.Exists(exe)) throw new Exception("SteamCMD download looked OK but steamcmd.exe is missing.");
            Ok("SteamCMD installed: " + exe);
        }

        static void DownloadWithSteamCmd(string defaultAccount)
        {
            foreach (string[] d in chosen.Depots)
            {
                string dir = DepotDir(d[0]);
                if (Directory.Exists(dir))
                {
                    Info("Removing old download: " + dir);
                    DeleteDir(dir);
                }
            }

            // SteamCMD keeps its own saved login, separate from the Steam app. We remember the
            // account name we last signed in with so returning users aren't asked for anything.
            string loginMarker = Path.Combine(steamCmdDir, "tf_lastlogin.txt");
            string remembered = null;
            try { if (File.Exists(loginMarker)) remembered = File.ReadAllText(loginMarker).Trim(); } catch { }
            bool hasCachedLogin = !string.IsNullOrEmpty(remembered);
            string suggested = hasCachedLogin ? remembered : defaultAccount;

            Info("Preparing to download Skyrim v" + chosen.Name + " (" + chosen.Depots.Length + " depots).");
            Console.WriteLine();
            string user = null;
            while (string.IsNullOrEmpty(user))
            {
                Theme.Use(Theme.Prompt);
                if (!string.IsNullOrEmpty(suggested))
                    Console.Write("   Steam account name" + (hasCachedLogin ? "" : " (your login name, not display name)") + " [Enter for '" + suggested + "']: ");
                else
                    Console.Write("   Steam account name (your login name, not display name): ");
                Theme.Use(Theme.Text);
                string input = (Console.ReadLine() ?? "").Trim();
                user = input.Length > 0 ? input : suggested;
            }

            bool returningUser = hasCachedLogin && string.Equals(user, remembered, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine();
            if (returningUser)
            {
                Ok("SteamCMD has signed in as '" + user + "' before, so it should NOT ask for a password");
                Info("or a Steam Guard code. It only asks again if that saved sign-in has expired.");
            }
            else
            {
                Info("Note: SteamCMD signs in separately from the Steam app, so having Steam open does");
                Info("not sign it in for you. The first time only, it needs your Steam password and -");
                Info("if Steam Guard is on - one Steam Guard code (from email or the Steam mobile app).");
                Info("You type these into SteamCMD below; this program never sees or stores them.");
                Info("After this first sign-in, SteamCMD remembers you and won't ask again.");
            }
            Info("The download is several GB, so it can take a while.");
            Console.WriteLine();

            List<string> parts = new List<string>();
            Log.Write("Running SteamCMD for version v" + chosen.Name + " as user '" + user + "'.");
            parts.Add("+login " + user);
            foreach (string[] d in chosen.Depots)
                parts.Add("+download_depot " + AppId + " " + d[0] + " " + d[1]);
            parts.Add("+quit");

            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(steamCmdDir, "steamcmd.exe"), string.Join(" ", parts.ToArray()));
            psi.UseShellExecute = false;         // run inside this console window
            psi.WorkingDirectory = steamCmdDir;

            Theme.Use(Theme.Dim);
            Console.WriteLine("---------------------------- SteamCMD ----------------------------");
            Theme.Use(Theme.Text);
            using (Process p = Process.Start(psi))
            {
                // Lore only starts once a depot folder exists (= login finished), so it never
                // gets in the way of the password / Steam Guard prompts.
                Lore.Start(15,
                    delegate () { return "-- " + (DownloadedBytes() / (1024 * 1024)) + " MB gathered so far --"; },
                    delegate () { return chosen.Depots.Any(d => Directory.Exists(DepotDir(d[0]))); });
                try { p.WaitForExit(); }
                finally { Lore.Stop(); }
            }
            Theme.Use(Theme.Dim);
            Console.WriteLine("------------------------------------------------------------------");
            Console.WriteLine();

            // Verify every depot actually arrived
            List<string> missing = new List<string>();
            foreach (string[] d in chosen.Depots)
            {
                string dir = DepotDir(d[0]);
                if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                    missing.Add(d[0]);
                else
                    Ok("Depot " + d[0] + " downloaded.");
            }
            if (missing.Count > 0)
                throw new Exception("SteamCMD didn't download depot(s) " + string.Join(", ", missing.ToArray()) +
                                    ".\n  Check the SteamCMD messages above (wrong password/Steam Guard code, or the account doesn't own Skyrim SE).");

            // Remember this account so the next run skips the password / Steam Guard messaging.
            try { File.WriteAllText(loginMarker, user); } catch { }
            Log.Write("SteamCMD sign-in succeeded; remembering account '" + user + "' for next time.");
        }

        // ------------------------------------------------------------------ copy

        static void CopyDepots()
        {
            Console.WriteLine();
            Step("Forging v" + chosen.Name + " files into " + gamePath);
            int total = 0;
            Lore.Start(10, null, null);
            try
            {

            foreach (string[] d in chosen.Depots)
            {
                string dir = DepotDir(d[0]);
                if (!Directory.Exists(dir))
                    throw new Exception("Download folder missing: " + dir);

                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                Step("Depot " + d[0] + " (" + files.Length + " files)");

                foreach (string src in files)
                {
                    string rel = src.Substring(dir.Length).TrimStart('\\', '/');
                    if (rel.StartsWith(".DepotDownloader", StringComparison.OrdinalIgnoreCase)) continue;
                    string dst = Path.Combine(gamePath, rel);
                    string dstDir = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(dstDir)) Directory.CreateDirectory(dstDir);

                    bool replacing = File.Exists(dst);
                    if (replacing)
                    {
                        FileAttributes a = File.GetAttributes(dst);
                        if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(dst, a & ~FileAttributes.ReadOnly);
                    }

                    File.Copy(src, dst, true); // overwrite = automatic "yes" to replace
                    Log.Write((replacing ? "[reforged] " : "[forged]   ") + rel);
                    string quip = Lore.CopyQuip();
                    lock (Theme.Sync)
                    {
                        Theme.Use(replacing ? Theme.Blue : Theme.Ok);
                        Console.Write(replacing ? "  [reforged] " : "  [forged]   ");
                        Theme.Use(Theme.Text);
                        Console.WriteLine(rel);
                        if (quip != null)
                        {
                            Theme.Use(Theme.Egg);
                            Console.WriteLine("               \\_ " + quip);
                            Theme.Use(Theme.Text);
                        }
                    }
                    total++;
                }
            }
            }
            finally { Lore.Stop(); }
            Ok(total + " files copied.");
            Info("(The downloads are still in " + contentRoot + " - delete that folder to free space.)");
        }

        // ------------------------------------------------------------------ helpers

        static long DownloadedBytes()
        {
            long sum = 0;
            foreach (string[] d in chosen.Depots)
            {
                try
                {
                    string dir = DepotDir(d[0]);
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { sum += new FileInfo(f).Length; } catch { }
                    }
                }
                catch { }
            }
            return sum;
        }

        static string DepotDir(string depot)
        {
            return Path.Combine(contentRoot, "depot_" + depot);
        }

        static void PrintVersion(string label)
        {
            try
            {
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(Path.Combine(gamePath, "SkyrimSE.exe"));
                Info(label + ": " + v.FileVersion);
            }
            catch { }
        }

        static bool AskYesNo(string question, bool defaultYes)
        {
            while (true)
            {
                Theme.Use(Theme.Prompt);
                Console.Write(question + (defaultYes ? " [Y/n]: " : " [y/N]: "));
                Theme.Use(Theme.Text);
                string a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                Log.Write("Prompt: " + question + " -> " + (a.Length == 0 ? (defaultYes ? "y" : "n") : a));
                if (a.Length == 0) return defaultYes;
                if (a == "y" || a == "yes") return true;
                if (a == "n" || a == "no") return false;
            }
        }

        static GameVersion FindVersionByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.TrimStart('v', 'V').Trim();
            foreach (GameVersion v in Versions)
                if (string.Equals(v.Name, n, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        static GameVersion ChooseVersion()
        {
            Console.WriteLine();
            Theme.Use(Theme.Dim);  Console.WriteLine("  " + new string('-', 64));
            Theme.Use(Theme.Blue); Console.WriteLine("   Which Skyrim Special Edition version do you want to install?");
            Theme.Use(Theme.Dim);  Console.WriteLine("  " + new string('-', 64));
            Console.WriteLine();

            for (int i = 0; i < Versions.Length; i++)
            {
                GameVersion v = Versions[i];
                Theme.Use(Theme.Prompt); Console.Write("   " + (i + 1) + ")  v" + v.Name);
                Theme.Use(Theme.Dim);    Console.WriteLine(i == Versions.Length - 1 ? "   (newest)" : (i == 0 ? "   (most compatible)" : ""));
                WriteBlurb(v.Blurb);
                Console.WriteLine();
            }

            Theme.Use(Theme.Info);
            Console.WriteLine("   Not sure? Check your modlist's requirements - it will usually name a");
            Console.WriteLine("   required game version. If you're starting fresh, v1.5.97 is the safest.");
            Console.WriteLine();
            Theme.Use(Theme.Text);

            while (true)
            {
                Theme.Use(Theme.Prompt);
                Console.Write("   Enter 1-" + Versions.Length + " (default 1 = v" + Versions[0].Name + "): ");
                Theme.Use(Theme.Text);
                string a = (Console.ReadLine() ?? "").Trim();
                GameVersion pick = null;
                int n;
                if (a.Length == 0) pick = Versions[0];
                else if (int.TryParse(a, out n) && n >= 1 && n <= Versions.Length) pick = Versions[n - 1];
                else pick = FindVersionByName(a);
                if (pick != null)
                {
                    Log.Write("Version chosen from menu: v" + pick.Name);
                    return pick;
                }
                Warn("Please enter a number between 1 and " + Versions.Length + ", or a version like 1.6.640.");
            }
        }

        static void WriteBlurb(string text)
        {
            int width = 80;
            try { width = Math.Max(40, Math.Min(84, Console.WindowWidth - 12)); } catch { }
            Theme.Use(Theme.Info);
            string cur = "";
            foreach (string w in text.Split(' '))
            {
                if (cur.Length > 0 && cur.Length + 1 + w.Length > width) { Console.WriteLine("        " + cur); cur = w; }
                else cur = cur.Length == 0 ? w : cur + " " + w;
            }
            if (cur.Length > 0) Console.WriteLine("        " + cur);
            Theme.Use(Theme.Text);
        }

        static void DeleteDir(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Warn("Couldn't remove " + dir + ": " + ex.Message);
            }
        }

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static bool RelaunchElevated()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--use-existing --version " + (chosen != null ? chosen.Name : ""));
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception) { return false; }
        }

        static void Banner()
        {
            Theme.Banner();
        }

        static void Line(Col c, string tag, string msg)
        {
            lock (Theme.Sync)
            {
                Theme.Use(c);
                Console.Write(tag);
                Theme.Use(Theme.Text);
                Console.WriteLine(msg);
            }
            Log.Write(tag.Trim() + " " + msg);
        }

        static void Step(string m) { Line(Theme.Blue, "[....] ", m); }
        static void Ok(string m)   { Line(Theme.Ok, "[ OK ] ", m); }
        static void Info(string m) { Line(Theme.Info, "[info] ", m); }
        static void Warn(string m) { Line(Theme.Warn, "[WARN] ", m); }
        static void Err(string m)  { Line(Theme.Err, "[FAIL] ", m); }
    }

    // ====================================================================== run log
    // Plain-text log written into the Skyrim folder so problems can be diagnosed afterwards.
    internal static class Log
    {
        static readonly object sync = new object();
        static readonly List<string> pending = new List<string>(); // buffered until the file is open
        static string path;

        public static string FilePath { get { return path; } }

        public static void Write(string text)
        {
            lock (sync)
            {
                string line = DateTime.Now.ToString("HH:mm:ss") + "  " + (text ?? "");
                if (path == null) { pending.Add(line); return; }
                try { File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8); } catch { }
            }
        }

        public static void Open(string dir)
        {
            lock (sync)
            {
                try
                {
                    string p = System.IO.Path.Combine(dir, "Tech-Forge Modlist Rescue.log");
                    StringBuilder head = new StringBuilder();
                    head.AppendLine("==================================================================");
                    head.AppendLine(" Tech-Forge's Modlist Rescue - run log");
                    head.AppendLine(" " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    head.AppendLine("==================================================================");
                    File.WriteAllText(p, head.ToString(), Encoding.UTF8);
                    foreach (string l in pending) File.AppendAllText(p, l + Environment.NewLine, Encoding.UTF8);
                    pending.Clear();
                    path = p;
                }
                catch { path = null; } // logging must never stop the tool from working
            }
        }
    }

    // ====================================================================== Skyrim flavour text
    // Shown while waiting for the depot downloads and file copies.
    // Original Tech-Forge writing set in the world of Skyrim (no game text reproduced).

    internal static class Lore
    {
        static readonly Random Rng = new Random();
        static Thread worker;
        static volatile bool running;
        static int patience = 14;
        static readonly List<string> tipBag = new List<string>();
        static readonly List<string> eggBag = new List<string>();

        static readonly string[] Tips = new string[]
        {
            "The Greybeards of High Hrothgar rarely speak, for their voices can shatter stone. Steam's servers are far less dangerous.",
            "Whiterun's Skyforge is said to be older than the city itself. This download is slightly newer.",
            "Dwemer machinery still hums in ruins across Skyrim, ages after its makers vanished. Your files should be done sooner.",
            "Half of Winterhold slid into the Sea of Ghosts during the Great Collapse. Only the College remained standing.",
            "Khajiit caravans travel between the holds. They are not welcome inside the cities, but they will always trade.",
            "Blackreach is a vast cavern beneath Skyrim, lit by glowing mushrooms the size of trees.",
            "Dragon Priests once ruled Skyrim in the name of the dragons. Their masks still hold great power.",
            "A Dragonborn can absorb the soul of a slain dragon and learn its Words of Power.",
            "Solitude is the capital of Skyrim, home of the Blue Palace and the Bards College.",
            "Markarth was carved from Dwemer stone. Its halls keep more secrets than the Jarl admits.",
            "Windhelm is one of the oldest cities in Skyrim, said to have been founded by Ysgramor himself.",
            "The Companions of Jorrvaskr trace their roots back to Ysgramor and his Five Hundred.",
            "Sovngarde is the Nordic afterlife, where the worthy feast forever in the Hall of Valor.",
            "Paarthurnax waits atop the Throat of the World, pondering what it means to be a dragon.",
            "The Falmer once lived on the surface as the Snow Elves. Now they lurk in the dark below.",
            "Giants herd mammoths across the tundra. Get too close to their camp and you may see Skyrim from above.",
            "Trolls heal quickly. Fire stops that. Nothing stops a slow internet connection, sadly.",
            "Daedric Princes rarely give gifts without a price. This rescue, however, is free.",
            "Alchemists learn an ingredient's hidden effects by combining it. Modders learn the same about plugins.",
            "Shrines to the Divines can cure diseases. Unfortunately, none of them cure crashes to desktop.",
            "A good load order is like a well-tempered blade: one bad file and the whole thing shatters.",
            "Many modlists need an exact game version. One Steam update and the whole list can stop working.",
            "Nords say a true warrior is judged by their deeds, not their patience. Today, it is patience.",
            "Riften's Thieves Guild has fallen on hard times. Some blame bad luck. Others whisper of Nocturnal.",
            "Ancient Nord tombs are full of draugr, traps, and puzzle doors. Check the claw before you try the rings.",
            "The Jarl of each hold rules from a great hall. Most of them will make you do their chores first.",
            "Snow covers much of Skyrim for most of the year. Your downloads, thankfully, are not affected by weather.",
            "Soul gems trap the souls of creatures to power enchantments. SteamCMD runs on electricity instead.",
        };

        static readonly string[] Eggs = new string[]
        {
            "Hey, you. You're finally awake. The download is still running, though.",
            "Your housecarl is sworn to carry your burdens. She has been handed three depots.",
            "A wheel of cheese rolled past the progress bar. It has been safely recovered.",
            "Someone stole a sweetroll. The guards are looking at you.",
            "M'aiq the Liar says: M'aiq once downloaded a depot in one second. M'aiq would never lie.",
            "Nazeem wants to know if you get to the Cloud District very often. He already knows the answer.",
            "FUS RO... no, wait, that won't make it download any faster.",
            "A dragon has been spotted over Whiterun! ...False alarm. It was a loading bar.",
            "Khajiit has files, if you have patience.",
            "A shopkeeper has a bucket on his head. Your files are being copied unnoticed.",
            "Somewhere, a horse is climbing a vertical cliff. Physics will allow it.",
            "Lockpick broke. Luckily, depots don't need lockpicks.",
            "The Dragonborn is buying 47 cabbages. Please stand by.",
            "You have been eaten by a mudcrab. Just kidding. The download continues.",
        };

        static readonly string[] CopyQuips = new string[]
        {
            "This file used to be an adventurer. Then it took an arrow to the knee.",
            "File placed carefully. No guards witnessed it.",
            "Item added: 1x Unenchanted File.",
            "Your Speech skill did not help. Copied anyway.",
            "This file has been blessed at the Shrine of Talos.",
            "A sweetroll was found inside this folder and quietly eaten.",
        };

        public static void Start(int intervalSeconds, Func<string> status, Func<bool> ready)
        {
            Stop();
            running = true;
            worker = new Thread(delegate ()
            {
                DateTime next = DateTime.Now.AddSeconds(5);
                while (running)
                {
                    if (ready != null && !ready()) { next = DateTime.Now.AddSeconds(5); Thread.Sleep(500); continue; }
                    if (DateTime.Now >= next)
                    {
                        string s = null;
                        try { if (status != null) s = status(); } catch { }
                        ShowNext(s);
                        next = DateTime.Now.AddSeconds(intervalSeconds);
                    }
                    Thread.Sleep(250);
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        public static void Stop()
        {
            running = false;
            if (worker != null) { worker.Join(2000); worker = null; }
        }

        static string Draw(List<string> bag, string[] source)
        {
            if (bag.Count == 0) bag.AddRange(source);
            int i = Rng.Next(bag.Count);
            string s = bag[i];
            bag.RemoveAt(i);
            return s;
        }

        public static void ShowNext(string status)
        {
            lock (Theme.Sync)
            {
                Console.WriteLine();
                if (status != null) { Theme.Use(Theme.Dim); Console.WriteLine("      " + status); }

                int roll = Rng.Next(100);
                if (roll < 15)
                {
                    WriteWrapped("  !  ", Draw(eggBag, Eggs), Theme.Egg);
                }
                else if (roll < 25)
                {
                    patience++;
                    WriteWrapped("  ^  ", "Your Patience skill has increased to " + patience + ".", Theme.Ok);
                }
                else
                {
                    WriteWrapped("  ~  ", Draw(tipBag, Tips), Theme.Lore);
                }
                Console.WriteLine();
                Theme.Use(Theme.Text);
                Log.Write("(waiting) " + (status != null ? status + " " : ""));
            }
        }

        // Occasionally returns a joke to print under a copied file (about 1 in 60 files)
        public static string CopyQuip()
        {
            lock (Theme.Sync)
            {
                if (Rng.Next(60) != 0) return null;
                return CopyQuips[Rng.Next(CopyQuips.Length)];
            }
        }

        static void WriteWrapped(string tag, string text, Col color)
        {
            int width = 90;
            try { width = Math.Max(40, Math.Min(90, Console.WindowWidth - 8)); } catch { }
            List<string> lines = new List<string>();
            string current = "";
            foreach (string word in text.Split(' '))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > width) { lines.Add(current); current = word; }
                else current = current.Length == 0 ? word : current + " " + word;
            }
            if (current.Length > 0) lines.Add(current);

            for (int i = 0; i < lines.Count; i++)
            {
                Theme.Use(Theme.Dim);
                Console.Write(i == 0 ? tag : "     ");
                Theme.Use(color);
                Console.WriteLine(lines[i]);
            }
        }
    }

    // ====================================================================== TECH-FORGE theme

    internal sealed class Col
    {
        public readonly int R, G, B;
        public readonly ConsoleColor Fallback;
        public Col(int r, int g, int b, ConsoleColor fallback) { R = r; G = g; B = b; Fallback = fallback; }
    }

    internal static class Theme
    {
        const string E = "\u001b";
        public static bool Vt;
        public static readonly object Sync = new object(); // keeps lines from different threads apart

        // Colours taken from the Tech-Forge logo
        public static readonly Col Bg     = new Col(11, 15, 23, ConsoleColor.Black);      // deep navy-black
        public static readonly Col Text   = new Col(200, 205, 214, ConsoleColor.Gray);    // brushed silver
        public static readonly Col Dim    = new Col(72, 80, 94, ConsoleColor.DarkGray);   // gunmetal
        public static readonly Col Prompt = new Col(245, 247, 250, ConsoleColor.White);
        public static readonly Col Blue   = new Col(0, 140, 250, ConsoleColor.Blue);      // logo blue
        public static readonly Col Ok     = new Col(90, 195, 255, ConsoleColor.Cyan);     // logo highlight blue
        public static readonly Col Info   = new Col(140, 148, 160, ConsoleColor.DarkCyan); // steel grey
        public static readonly Col Warn   = new Col(255, 190, 70, ConsoleColor.Yellow);
        public static readonly Col Err    = new Col(255, 85, 85, ConsoleColor.Red);
        public static readonly Col Lore   = new Col(214, 192, 140, ConsoleColor.DarkYellow); // old parchment
        public static readonly Col Egg    = new Col(150, 215, 255, ConsoleColor.Cyan);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr h, out uint mode);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr h, uint mode);

        public static void Init()
        {
            try { Console.Title = "TECH-FORGE's Modlist Rescue  |  Skyrim Special Edition"; } catch { }
            try
            {
                IntPtr h = GetStdHandle(-11);
                uint mode;
                if (GetConsoleMode(h, out mode) && SetConsoleMode(h, mode | 0x0004)) Vt = true; // virtual terminal colours
            }
            catch { Vt = false; }

            try
            {
                if (Console.WindowWidth < 100 && Console.LargestWindowWidth >= 100)
                {
                    if (Console.BufferWidth < 100) Console.BufferWidth = 100;
                    Console.WindowWidth = 100;
                }
            }
            catch { }

            if (Vt)
            {
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
                Console.Write(E + "[0m" + BgCode(Bg) + FgCode(Text) + E + "[2J" + E + "[3J" + E + "[H");
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Theme.Use(Theme.Text);
                try { Console.Clear(); } catch { }
            }
        }

        public static void Use(Col c)
        {
            if (Vt) Console.Write(FgCode(c));
            else Console.ForegroundColor = c.Fallback;
        }

        public static void Reset()
        {
            if (Vt) Console.Write(E + "[0m");
            else Console.ResetColor();
        }

        static string FgCode(Col c) { return E + "[38;2;" + c.R + ";" + c.G + ";" + c.B + "m"; }
        static string BgCode(Col c) { return E + "[48;2;" + c.R + ";" + c.G + ";" + c.B + "m"; }
        static string FgHex(string h) { return E + "[38;2;" + Convert.ToInt32(h.Substring(0, 2), 16) + ";" + Convert.ToInt32(h.Substring(2, 2), 16) + ";" + Convert.ToInt32(h.Substring(4, 2), 16) + "m"; }
        static string BgHex(string h) { return E + "[48;2;" + Convert.ToInt32(h.Substring(0, 2), 16) + ";" + Convert.ToInt32(h.Substring(2, 2), 16) + ";" + Convert.ToInt32(h.Substring(4, 2), 16) + "m"; }

        // ------------------------------------------------------------------ banner

        public static void Banner()
        {
            Console.WriteLine();
            if (Vt)
            {
                DrawWordmark(2);
            }
            else
            {
                Use(Text); Console.Write("   TECH");
                Use(Blue); Console.WriteLine("-FORGE");
            }
            Console.WriteLine();
            Use(Dim);  Console.WriteLine("  " + new string('-', 64));
            Use(Blue); Console.Write("   M O D L I S T   R E S C U E");
            Use(Text); Console.Write("   for Skyrim Special Edition");
            Use(Dim);  Console.WriteLine("   |   v1.0");
            Use(Dim);  Console.WriteLine("  " + new string('-', 64));
            Use(Text);
            Console.WriteLine();
        }

        // Draws the TF emblem with half-block characters: each console cell shows 2 vertical pixels
        static void DrawEmblem(int indent)
        {
            string bg = BgCode(Bg);
            for (int y = 0; y + 1 < Logo.Length; y += 2)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(bg).Append(new string(' ', indent));
                string top = Logo[y], bottom = Logo[y + 1];
                for (int x = 0; x < top.Length; x += 6)
                {
                    string t = top.Substring(x, 6), b = bottom.Substring(x, 6);
                    bool tt = t == "......", bt = b == "......";
                    if (tt && bt) sb.Append(bg).Append(' ');
                    else if (bt) sb.Append(bg).Append(FgHex(t)).Append('\u2580');
                    else if (tt) sb.Append(bg).Append(FgHex(b)).Append('\u2584');
                    else sb.Append(BgHex(b)).Append(FgHex(t)).Append('\u2580');
                }
                sb.Append(bg);
                Console.WriteLine(sb.ToString());
            }
        }

        // Big "TECH-FORGE" lettering: TECH in silver, -FORGE in blue, with a top-to-bottom gradient
        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { 'T', new string[] { "#####", "  #  ", "  #  ", "  #  ", "  #  " } },
            { 'E', new string[] { "#####", "#    ", "#### ", "#    ", "#####" } },
            { 'C', new string[] { " ####", "#    ", "#    ", "#    ", " ####" } },
            { 'H', new string[] { "#   #", "#   #", "#####", "#   #", "#   #" } },
            { '-', new string[] { "    ", "    ", "### ", "    ", "    " } },
            { 'F', new string[] { "#####", "#    ", "#### ", "#    ", "#    " } },
            { 'O', new string[] { " ### ", "#   #", "#   #", "#   #", " ### " } },
            { 'R', new string[] { "#### ", "#   #", "#### ", "#  # ", "#   #" } },
            { 'G', new string[] { " ####", "#    ", "#  ##", "#   #", " ####" } },
        };

        static readonly string[] SilverRows = { "eef0f4", "d4d8de", "b9bec6", "a0a6af", "868d97" };
        static readonly string[] BlueRows   = { "3fb6ff", "1e94ff", "0a78fa", "0563f0", "0050dc" };

        static void DrawWordmark(int indent)
        {
            const string word = "TECH-FORGE";
            for (int row = 0; row < 5; row++)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(BgCode(Bg)).Append(new string(' ', indent));
                for (int i = 0; i < word.Length; i++)
                {
                    sb.Append(FgHex(i < 4 ? SilverRows[row] : BlueRows[row]));
                    sb.Append(Glyphs[word[i]][row].Replace('#', '\u2588')).Append(' ');
                }
                Console.WriteLine(sb.ToString());
            }
        }

        // TF emblem pixels (RRGGBB, "......" = transparent), generated from the Tech-Forge logo
        static readonly string[] Logo = new string[]
        {
            "..................666d7460686e666f766e757f737a84767f87828891858b94888f988e949d9499a29ba0aaa3aab2a8acb5afb4bdb7bcc4bfc5ccc3c9cfc8cdd4cacbd0d8dce485909a............3554683daad635b9eb35b8ea35bdeb36beec37c0ed37c1ef34c0ee34c0ef33c0ef33c1ef32c0ef31c2ef31c1ee2fbfee2ebfee2cc4ee2cc3ea2ed7f6388a9d",
            "............4a4f584249543b424b424a544d565f4d565f5660695f6871646d766d7680747d877d858f879099959ca69ba2ada2a9b3adb4bfb7bec7bfc4cdc2c7d1cdd0d5b1bcc6............294c5c1fb6fa00aeff00adff00afff00b2ff00b3ff00b4ff00b6ff00b3ff00b4ff00b3ff00b2fe00b1ff00b1ff00b2ff00b1ff00b2fd00b5fc00c7ff0eb4d9......",
            "......32373f475058363d473d444e42495347505949525b4f5861575f68616a7369717a6f788178818a838c95949ba59ca3ada3aab4a9b0bab4bbc4bcc2cac3c6cdcdd6e1414b54............199bdf009efe03a0f902a3fb02a4fa03a8fc02a8fa02a9fa02a9fb02a8fb02a9fb01a8fa02a6fb02a4fb03a4fd02a4fa02a5fa02a6f901aef80bcefb153d4a......",
            "......4950592e3740343b4439404a3f4650424b544750594d575f555e67626b746770796a737d767f88828b949098a199a0aaa0a7b1abb2bcb4bbc4bac0c8d0d6e175808b............1e78ab0294ff018ff7019afd009bfa00a3ff00a6ff00a7ff00a9ff00aaff00acff00adff00aeff00aeff00abff00aaff00abff00adff00afff06cfff15748c............",
            "40474e353f4a3239433a424c404852464e574b535c515962575f695d656e61697369717a6e778278818b818a938a929b969fa89fa7b1a1a9b3a3abb4b1b8c398a1ad............194b6a088dff0080f9018dfa028cfb039aff0995d90893d20896d50998d4099bd3089ed408a2d809a7d909a9d909a9d908a9d909acda0aabd909b3db129cb6..................",
            "181d25121820131821141820151921161a22161a22171b24181c25181c25181c25191d26......53596189919b878e98969ea998a1a92b30381a1f271f242d..................0b7beb006efc017cfa007ff90293ff0e72a6............................................................................................................",
            "..............................................................................6d757d7f8793888d989199a59aa0a7..............................0e3c6d026cff0061f3016ff7007fff0b80d3..................................................................................................................",
            "........................................................................4f575f757f8a737a86868d999299a5979ea6..............................0c386a0364fe005bf20062f8077fef103242..................................................................................................................",
            "........................................................................616a736b7580767f8a7c848f8a909c949ca4..............................0e386d0361ff0150ec046eff0f456e........................................................................................................................",
            "........................................................................5b646d6a747f6f778379818c838995939aa3..............................0b35690259f80154f40d52a1............153e541e80c11b80bb1c85bd1c89c1208ec02095c31a98c51896c51899c51599c419abd0143846....................................",
            "........................................................................5b636d6a727d6c748079818b848b968b949c......0e243b102a42............0c33660255fe0a5bc8..................0b84e80081ff0089ff0090ff0095ff009bff00a1ff00a4ff00a5ff00a6ff00c7ff0b8aaf..........................................",
            "........................................................................5a616b636b7668707b747c867b838e889099......0c3e6f11457e............0b31660864f00f2942............0e6cbe006eff016ff40176f80177f6017ef70185f80188f7018bf8018df501a0fc0aa8da................................................",
            "........................................................................555c665d6570626a756e7580767e89858e98......082b580e3366............0e376e0e427c............10568e0166fc015bee0269f50068f7006cfe0074ff007dff0082ff0087ff0091ff05b5fb0e3f4d................................................",
            "........................................................................505861575f6a5c646f646c776e76817b858f......0b1f3f101f360b21430c2242..................113e5f0661ea004de3025aec015eee026dfc0778e5067ce10683e1068be2068ee106a5e80e6783......................................................",
            "........................................................................4c535c505862555e675d667167707a7b858e......0821430f24400c28580d2d60..................0c51b1004de4024cd50250e1015ef70d60ac................................................................................................",
            "........................................................................4851584a525c515a63555e67616a73717a85......081f410e294d1030660e346d..................0b47a5014bdf024cda004ede0865d8......................................................................................................",
            "........................................................................454c56454d5748505a4e5761575f6869727c......081d3d0d203e..............................0c47a50048d30147ce014adf0f5fbd......................................................................................................",
            "........................................................................444a52404752454c565058624f5861667179......081c3f0e2447............0c397f0d2958......0d46a40041ca0242c40047d70e5dbc......................................................................................................",
            "........................................................................4146503d444f404750464e594d555f5b646e............0c2d620f1e390b1a360c2f720b2045......0c40990038b90339b5003ec70f5ab5......................................................................................................",
            "........................................................................3e444e353d463a414b434b55444c565e677133373f......0c1e3f0d2a620c19310a214c............0c3a900035ae0232a40037bb1055b1......................................................................................................",
            "........................................................................272e373b434d353c463e454f454e564b545e62676e............0d23550c16310b2654............0b3e92002d9b01309b0032b10e52ac......................................................................................................",
            "..............................................................................31354239404b363f46434c554049524e5862393f46......0d224e0a132a0c2455......11405c043794032488012990023eb4104176......................................................................................................",
            "....................................................................................373d4a353e48363f483c454f48525b333a40......0d22510b13290a1b46102a46073886001e7901258101309a0d4488............................................................................................................",
            "....................................................................................181c253a414c303a42343e47444c5730333d......0d2153......0b1a3b0c225203247d01206e00227d0a4393..................................................................................................................",
            "..........................................................................................22252f3a414d2e363f3d46502f333d......0c1f4f......0c1b420e1f4d032577001968073789102a42..................................................................................................................",
            "................................................................................................272c3532394438424a2e333d......0b1b3e0c17360b193d0e1f40031e700328770e3358........................................................................................................................",
            "......................................................................................................2c303a3a424e252b33......0c193d091b47......0c1a3d03287d0e3864..............................................................................................................................",
            "............................................................................................................323945282d38......0c193d0a193d......0a1e400c4183....................................................................................................................................",
            "............................................................................................................181c26232932......0b1839091b46......0d223e0d2038....................................................................................................................................",
            "..............................................................................................................................0d152e0b172e......................................................................................................................................................"
        };
    }
}
