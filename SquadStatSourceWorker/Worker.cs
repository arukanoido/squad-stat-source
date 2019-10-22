using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Tokens;

namespace SquadStatSourceWorker
{
    public class DedicatedServer
    {
        public string ID { get; set; }
        public string Index { get; set; }
        public string Path { get; set; }
        public string LastTimestamp { get; set; }
    }

    public class Worker
    {
#if DEBUG
        public static string Appsettings = "appsettings.dev.json";
#endif
#if RELEASE
        public static string Appsettings = "appsettings.json";
#endif
        public static JObject Config;
        public static DedicatedServer DedicatedServer = new DedicatedServer();
        public static Uploader Uploader;

        public static Tokenizer Tokenizer = new Tokenizer()
            .RegisterValidator<Tokens.Validators.NotEqualValidator>()
            .RegisterValidator<Tokens.Validators.EqualsValidator>()
            .RegisterTransformer<Tokens.Transformers.ChopEndTransformer>();

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.Unicode;
            
            Squad.Init();

            string ID = args[0];
            Squad.Server.ServerID = Convert.ToInt64(ID);
            string Path = args[1] + @"\SquadGame\Saved\Logs\";

            Config = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(Appsettings)) as JObject;

            Uploader = new Uploader(Config);

            int ServerIndex = 0;
            foreach (var Server in Config["servers"])
            {
                if (Convert.ToInt32(Server["id"].ToString()) == Squad.Server.ServerID)
                {
                    DedicatedServer.ID = ID;
                    DedicatedServer.Index = ServerIndex.ToString();
                    DedicatedServer.Path = Path;
                    DedicatedServer.LastTimestamp = (string)Server["lasttimestamp"];
                }
                ServerIndex++;
            }

            DateTime LastTimestamp = new DateTime();
            DateTime.TryParseExact(DedicatedServer.LastTimestamp, "yyyy.MM.dd-HH.mm.ss:fff", null, DateTimeStyles.None, out LastTimestamp);

            var LogFiles = Directory.GetFiles(Path).OrderBy(f => f);
            foreach (var LogFile in LogFiles)
            {
                Stopwatch Elapsed = new Stopwatch();
                string Filename = System.IO.Path.GetFileNameWithoutExtension(LogFile);
                var Pattern = @"SquadGame-backup-{Timestamp}";
                var Tokenized = new Tokenizer().Tokenize(Pattern, Filename);
                if (Tokenized.Success)
                {
                    var FileTimestamp = DateTime.ParseExact(Tokenized.Tokens.Matches[0].Value.ToString(), "yyyy.MM.dd-HH.mm.ss", null);
                    if (LastTimestamp <= FileTimestamp)
                    {
                        ReadBackupLogFile(LogFile, LastTimestamp, Elapsed);
                        Console.WriteLine(DateTime.Now.ToString("h:mm:ss tt") + " - Server " + ID 
                            + " - Read: " + System.IO.Path.GetFileName(LogFile) 
                            + " in " + Elapsed.Elapsed.TotalMilliseconds.ToString("N1") + "0ms");
                    }
                }
            }
            
            string NewLog = "SquadGame.log";
            var Waiter = new AutoResetEvent(false);
            var Watcher = new FileSystemWatcher(Path) 
            {
                Filter = NewLog,
                EnableRaisingEvents = true
            };
            Watcher.Changed += (s,e) => Waiter.Set();

            var FileHandle = new FileStream(Path + NewLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var BufferedStreamHandle = new BufferedStream(FileHandle);
            using (var Reader = new StreamReader(BufferedStreamHandle))
            {
                Console.WriteLine(DateTime.Now.ToString("h:mm:ss tt") + " - Server " + ID + " - Reading: " + NewLog);
                var Line = SeekTimestamp(Reader, LastTimestamp);
                
                while (true)
                {
                    if (Line != null)
                    {
                        if (Line.Length > 30)
                        {
                            Parser.Parse(Line, Events.List);
                        }
                    }
                    else
                    {
                        Waiter.WaitOne(1000);
                    }
                    Line = Reader.ReadLine();
                }
            }
        }

        static string SeekTimestamp(StreamReader Reader, DateTime TimestampToSeek)
        {
            // While seeking, we still need to register player joins and disconnects
            Event[] ShortList = new List<Event>(Events.List).GetRange(0, 5).ToArray();
            string Line;
            while ((Line = Reader.ReadLine()) != null)
            {
                if (Line.Length > 30 && Line.StartsWith("[", StringComparison.Ordinal))
                {
                    DateTime Timestamp = DateTime.ParseExact(Line.Substring(1, 23), "yyyy.MM.dd-HH.mm.ss:fff", null);
                    if (Timestamp > TimestampToSeek) 
                    { 
                        Serializer.Clear(); 
                        break; 
                    }
                    else { Parser.Parse(Line, ShortList); }
                }
            }
            return Line;
        }

        static void ReadBackupLogFile(string LogFile, DateTime Timestamp, Stopwatch Elapsed)
        {
            using (var FileHandle = File.Open(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var BufferedStreamHandle = new BufferedStream(FileHandle))
                {
                    using (var Reader = new StreamReader(BufferedStreamHandle, Encoding.UTF8, true))
                    {
                        Elapsed.Start();
                        string Line = SeekTimestamp(Reader, Timestamp);

                        if (Line == null) { return; }
                        string LastLine = Line;
                        do
                        {
                            if (Line.Length > 30)
                            {
                                Parser.Parse(Line, Events.List);
                            }
                            LastLine = Line;
                        }
                        while ((Line = Reader.ReadLine()) != null);

                        // Write server close event to trigger serialization
                        var Event = new ServerClosed();
                        Event.Parse(LastLine);

                        Elapsed.Stop();
                    }
                }
            }
        }

        public static void WriteDedicatedServerConfig(string Key, string Value)
        {
            var FilePath = Appsettings;
            var Json = File.ReadAllText(FilePath);
            var Jobject = Newtonsoft.Json.JsonConvert.DeserializeObject(Json) as JObject;
            var Jtoken = Jobject.SelectToken("servers[" + DedicatedServer.Index + "]." + Key);
            Jtoken.Replace(Value);
            var OutJson = Jobject.ToString();
            File.WriteAllText(FilePath, OutJson);
        }
    }
}
