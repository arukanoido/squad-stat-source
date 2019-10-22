using System;
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SquadStatSource
{
    class DedicatedServer
    {
        public string ID { get; set; }
        public string Path { get; set; }
        public string LastTimestamp { get; set; }
    }

    class Program
    {
#if DEBUG
        public static string Appsettings = "appsettings.dev.json";
#endif
#if RELEASE
        public static string Appsettings = "appsettings.json";
#endif
        public static JObject Config;

        private static List<DedicatedServer> DedicatedServers = new List<DedicatedServer>();

        static void Main(string[] args)
        {
            Config = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(Appsettings)) as JObject;

            HttpClient Client = new HttpClient();
            foreach (var Server in Config["servers"])
            {
                var DedicatedServer = new DedicatedServer()
                {
                    ID = (string)Server["id"],
                    Path = (string)Server["path"],
                };

                var Task = CheckServerID(DedicatedServer.ID);
                if (!Task.Result)
                {
                    Console.WriteLine("ERROR: Invalid server id (" + DedicatedServer.ID + ") in appsettings.json");
                    return;
                }
                DedicatedServers.Add(DedicatedServer);
            }

            string WorkerPath = "SquadStatSourceWorker.dll";

#if DEBUG
            WorkerPath = @"bin/Debug/netcoreapp2.2/SquadStatSourceWorker.dll";
#endif

            Process Worker = null;
            foreach (var Server in DedicatedServers)
            {
                var Info = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    FileName = "dotnet",
                    Arguments = WorkerPath + " " + Server.ID + " " + Server.Path
                };
                Worker = Process.Start(Info);
            }
            // Doesn't matter which one we wait for
            Worker.WaitForExit();
        }

        static async Task<bool> CheckServerID(string ID)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync("https://api.battlemetrics.com/servers/" + ID);
                return response.IsSuccessStatusCode;
            }
        }
    }
}
