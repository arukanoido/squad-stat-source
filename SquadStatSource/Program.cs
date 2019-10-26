using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Threading;
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

        public AnonymousPipeServerStream Sender;
        public AnonymousPipeServerStream Receiver;
        public StreamWriter SenderWrite;
        public bool Running;
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

        public static Uploader Uploader;

        static void Main(string[] args)
        {
            Config = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(Appsettings)) as JObject;

            Uploader = new Uploader(Config);

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

                if (!File.Exists(DedicatedServer.Path + @"\SquadGame\Saved\Logs\SquadGame.log"))
                {
                    Console.WriteLine("ERROR: Invalid server path (" + DedicatedServer.Path + ") in appsettings.json");
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
                Server.Sender = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
                Server.Receiver = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                Server.Running = true;

                var Info = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    FileName = "dotnet",
                    Arguments = WorkerPath 
                        + " " + Server.Sender.GetClientHandleAsString() 
                        + " " + Server.Receiver.GetClientHandleAsString()
                };
                Worker = Process.Start(Info);

                Server.Sender.DisposeLocalCopyOfClientHandle();
                Server.Receiver.DisposeLocalCopyOfClientHandle();

                Server.SenderWrite = new StreamWriter(Server.Sender);
                Server.SenderWrite.AutoFlush = true;
                Server.SenderWrite.WriteLine("SYNC");
                Server.Sender.WaitForPipeDrain();

                Server.SenderWrite.WriteLine(Server.ID + " " + Server.Path);
                Server.Sender.WaitForPipeDrain();

                new Thread(delegate ()
                {
                    using (StreamReader Reader = new StreamReader(Server.Receiver))
                    {
                        String Received;
                        while (Server.Running && Server.Receiver.IsConnected)
                        {
                            Received = Reader.ReadLine();
                            if (Received != null) { Uploader.UploadDiff(Received); }
                        }
                        Server.Running = false;
                    }
                }).Start();
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
