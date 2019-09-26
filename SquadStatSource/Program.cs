using System;
using System.IO;
using System.Diagnostics;
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
        public static JObject Config;

        private static List<DedicatedServer> DedicatedServers = new List<DedicatedServer>();

        static void Main(string[] args)
        {
            Config = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText("appsettings.json")) as JObject;

            foreach (var Server in Config["servers"])
            {
                var DedicatedServer = new DedicatedServer()
                {
                    ID = (string)Server["id"],
                    Path = (string)Server["path"],
                };
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
    }
}
