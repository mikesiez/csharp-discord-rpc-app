using System.Text.Json;
using DiscordRPC;
using System.Text.RegularExpressions;
using System.Diagnostics;

class RobloxLogHelper
{
    private static string logsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "logs"
    );

    // Get the most recent log file
    public static string[] GetLatestLogFile()
    {
        if (!Directory.Exists(logsRoot))
            throw new Exception("Roblox logs folder not found!");

        var logFiles = new DirectoryInfo(logsRoot)
            .GetFiles("*.log")
            .OrderByDescending(f => f.LastWriteTime)
            .ToArray();

        if (logFiles.Length == 0)
            throw new Exception("No log files found!");

        if (logFiles.Length == 1)
        {
            return [logFiles[0].FullName];
        } // else we have more than 1 file so read em

        return [logFiles[0].FullName,logFiles[1].FullName];
    }

    public static string GetExperienceId(string[] logFilePathL) // need to make log caching and/or read more lines to avoid games logging a lot of things causing rpc death
    {
        string[] lines;

        {
            var allLines = new List<string>();
         
            if (logFilePathL.Length == 2)
            {
                using var stream1 = new FileStream(logFilePathL[1], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader1 = new StreamReader(stream1);
                while (!reader1.EndOfStream)
                    allLines.Add(reader1.ReadLine()!);
            }

            using var stream0 = new FileStream(logFilePathL[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader0 = new StreamReader(stream0);
            while (!reader0.EndOfStream)
                allLines.Add(reader0.ReadLine()!);


            lines = allLines.TakeLast(500).ToArray();
        }

        Regex placeIdRegex = new Regex(@"placeid:(\d+)", RegexOptions.IgnoreCase); // Regex matches "placeid:123456789" (case insensitive)
        Regex connectionLostRegex = new Regex(@"Client:Disconnect", RegexOptions.IgnoreCase);

        // Go through lines in reverse (most recent first)
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (connectionLostRegex.IsMatch(lines[i]))
            {
                // Connection lost occurred before finding placeid
                return "NotInExperience";
            }

            Match match = placeIdRegex.Match(lines[i]);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null; // nothing found
    }


    public static string GetUserId(string[] logFilePathL)
    {
        string[] lines;

        {
            var allLines = new List<string>();
         
            if (logFilePathL.Length == 2)
            {
                using var stream1 = new FileStream(logFilePathL[1], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader1 = new StreamReader(stream1);
                while (!reader1.EndOfStream)
                    allLines.Add(reader1.ReadLine()!);
            }

            using var stream0 = new FileStream(logFilePathL[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader0 = new StreamReader(stream0);
            while (!reader0.EndOfStream)
                allLines.Add(reader0.ReadLine()!);


            lines = allLines.TakeLast(500).ToArray();
        }

        // Regex matches "userid:123456789" (case insensitive)
        Regex userIdRegex = new Regex(@"userid:(\d+)", RegexOptions.IgnoreCase);

        // Go through lines in reverse (most recent first)
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            Match match = userIdRegex.Match(lines[i]);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null; // nothing found
    }

}

class Program
{
    static HttpClient client = new HttpClient();
    static JsonDocument json;
    static DiscordRpcClient discord;

    static long CLIENT_ID = 11; // discord application ID 

    static string userID = "";

    static string[] logFileInUse;

    static async Task Main()
    {
        var trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Roblox RPC"
        };
        trayIcon.MouseClick += (s, e) => {
            if (MessageBox.Show("Stop Roblox RPC?", "Roblox RPC",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                trayIcon.Visible = false;
                Environment.Exit(0);
            }
        };

        logFileInUse =  RobloxLogHelper.GetLatestLogFile();
        userID = RobloxLogHelper.GetUserId(logFileInUse);

        // Run your loop on a background thread
        _ = Task.Run(async () => await AppLoop());

        // Keep main thread alive pumping UI messages
        Application.Run();
        
    }

    static async Task AppLoop()
    {
        logFileInUse = RobloxLogHelper.GetLatestLogFile();
        while (true)
        {
            if (Process.GetProcessesByName("RobloxPlayerBeta").Length == 0)
            {
                await Task.Delay(1500);
                continue;
            }
            await RPCLoop();

            Console.WriteLine("Roblox closed");
            
            await Task.Delay(3000);
        }
    }
    static async Task RPCLoop()
    {
        string prevExperience = "";

        discord = new DiscordRpcClient(CLIENT_ID.ToString());
        discord.Initialize();
        
        while (Process.GetProcessesByName("RobloxPlayerBeta").Length > 0)
        {
            logFileInUse = RobloxLogHelper.GetLatestLogFile();
            string experienceId = RobloxLogHelper.GetExperienceId(logFileInUse);

            if (prevExperience == experienceId)
            {
                await Task.Delay(3000);
                continue;
            } 
            prevExperience = experienceId;

            
            if (experienceId == "NotInExperience") // we are not in a game
            {
                Console.WriteLine("Not in experience");

                discord.SetPresence(new RichPresence
                {
                    Details = "Browsing games",
                    State = "In the Roblox App",
                    Timestamps = Timestamps.Now,
                    Assets = new Assets
                    {
                        LargeImageKey = "pfp",
                        LargeImageText = "In Roblox Menu",
                    },
                    Buttons = new DiscordRPC.Button[]
                    {
                        new DiscordRPC.Button { Label = "Visit profile", Url = $"https://www.roblox.com/users/{userID}/profile" },
                        new DiscordRPC.Button { Label = "Visit website", Url = $"https://www.roblox.com/"}
                    }
                });

            }

            else if (experienceId != null) // we are in experience
            {
                Console.WriteLine("Current Roblox experience ID: " + experienceId);
                
                // get experience img
                string img_url =
                    $"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={experienceId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false";
                string ImgResponse = await client.GetStringAsync(img_url);
                json = JsonDocument.Parse(ImgResponse);
                string image = json.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("imageUrl")
                    .GetString()!;

                // get experience's universe so we can get full experience data
                string universeIdReq = $"https://apis.roblox.com/universes/v1/places/{experienceId}/universe";
                string universeIdData = await client.GetStringAsync(universeIdReq);
                json = JsonDocument.Parse(universeIdData);
                string universeId = json.RootElement.GetProperty("universeId").GetInt64().ToString();
                

                string experienceInfoReq = $"https://games.roblox.com/v1/games?universeIds={universeId}";
                string experienceInfoData = await client.GetStringAsync(experienceInfoReq);
                json = JsonDocument.Parse(experienceInfoData);
                
                string experienceTitle = json.RootElement.GetProperty("data")[0].GetProperty("name").GetString();
                string experiencePcount = json.RootElement.GetProperty("data")[0].GetProperty("playing").GetInt64().ToString();

                Console.WriteLine("Successfully gotten information from API calls");

                discord.SetPresence(new RichPresence
                {
                    Details = $"{experienceTitle}",
                    State = $"{experiencePcount} active players.",
                    Timestamps = Timestamps.Now,
                    Assets = new Assets
                    {
                        LargeImageKey = image,
                        LargeImageText = $"Playing {experienceTitle}",
                        SmallImageKey = "pfp",
                        SmallImageText = "Playing Roblox"
                    },
                    Buttons = new DiscordRPC.Button[]
                    {
                        new DiscordRPC.Button { Label = "Visit profile", Url = $"https://www.roblox.com/users/{userID}/profile" },
                        new DiscordRPC.Button { Label = "Visit game page", Url = $"https://www.roblox.com/games/{experienceId}"}
                    }
                });

            }

            else
            {
                Console.WriteLine("No relevant information found in log.");
                await Task.Delay(3000);
                continue;
            }

            await Task.Delay(5000);
        }

        await Task.Delay(3000);
        discord.ClearPresence();
        discord.Dispose();

        return;
    }

}

