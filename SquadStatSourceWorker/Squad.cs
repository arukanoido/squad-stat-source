using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Force.Crc32;
using DuoVia.FuzzyStrings;

namespace SquadStatSourceWorker
{
    public static class Squad
    {
        public static Server Server = new Server();
        public static Dictionary<long, Player> Players = new Dictionary<long, Player>();
        public static Dictionary<long, SquadType> Maps = new Dictionary<long, SquadType>();
        public static Dictionary<long, SquadType> Factions = new Dictionary<long, SquadType>();
        public static Dictionary<long, SquadType> Roles = new Dictionary<long, SquadType>();
        public static Dictionary<long, SquadType> Weapons = new Dictionary<long, SquadType>();
        public static Dictionary<long, SquadType> Vehicles = new Dictionary<long, SquadType>();

        public static void Init()
        {
            JObject Squad = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText("squad.json")) as JObject;
            foreach (var JItem in Squad["weapons"])
            {
                var Weapon = new Weapon(JItem["class"].ToString());
                Weapons.Add(Weapon.ID, Weapon);
            }
            foreach (var JItem in Squad["teams"])
            {
                var Faction = new Faction(JItem["name"].ToString())
                {
                    Abbreviation = JItem["abbreviation"].ToString()
                };
                Factions.TryAdd(Faction.ID, Faction);
            }
            foreach (var JItem in Squad["roles"])
            {
                var Role = new Role(JItem["name"].ToString());
                Roles.TryAdd(Role.ID, Role);
            }
            foreach (var JItem in Squad["vehicles"])
            {
                var Vehicle = new Vehicle(JItem["class"].ToString());
                Vehicles.Add(Vehicle.ID, Vehicle);
            }
        }

        public static long GetItemOrDefault(string Name, Dictionary<long, SquadType> Collection, bool RequireValid = false, bool Fuzzy = false)
        {
            long Result = -1L;
            if (Fuzzy)
            {
                Result = GetFuzzyMatch(Name, Collection);
            }
            else 
            {
                Result = GetExactMatch(Name, Collection);
            }
            if (RequireValid) { Debug.Assert(Result != -1); }
            return (Result == -1) ? GetCRC32OfInput(Name): Result;
        }

        public static long GetExactMatch(string Name, Dictionary<long, SquadType> Collection)
        {
            var ID = GetCRC32OfInput(Name);
            return (Collection.ContainsKey(ID)) ? ID: -1L;
        }

        public static long GetFuzzyMatch(string Name, Dictionary<long, SquadType> Collection)
        {
            var Best = Collection.Select(Pair => new {
                    Item = Pair,
                    Score = Pair.Value.Name.LevenshteinDistance(Name)
                })
                .Aggregate(
                    (agg, next) => next.Score < agg.Score ? next : agg
                );
            return Best.Item.Key;
        }

        public static string TrimIDFromName(string Name)
        {
            if (Name.LastIndexOf("_C") + 2 == Name.Length) { return Name; }
            return Name.Remove(Name.LastIndexOf("_C") + 2);
        }

        public static long GetCRC32OfInput(string Input)
        {
            return Convert.ToInt64(Crc32Algorithm.Compute(Encoding.ASCII.GetBytes(Input)));
        }
    }

    public class Server
    {
        public long ServerID { get; set; }
        public Match CurrentMatch { get; set; }
        public List<long> PlayersOnServer = new List<long>();
        //public string Line = "";

        public static long FindPlayerByName(string Name)
        {
            var Player = Squad.Server.PlayersOnServer
                .Join(Squad.Players,
                    SteamID => SteamID,
                    Players => Players.Key,
                    (SteamID, Players) => new { SteamID, Players.Value })
                .FirstOrDefault(Pair => Pair.Value.Name.Equals(Name));
            if (Player == null) { return -1; }
            return Player.SteamID;
        }

        public static long FindPlayerByFullName(string FullName, out string Prefix)
        {
            string PrefixString = null;
            var Player = Squad.Server.PlayersOnServer
                .Join(Squad.Players,
                    SteamID => SteamID,
                    Players => Players.Key,
                    (SteamID, Players) => new { SteamID, Players.Value })
                .FirstOrDefault(Pair => {
                    if (FullName.Length < Pair.Value.Name.Length) { return false; }
                    int StartIndex = FullName.Length - Pair.Value.Name.Length;
                    if (StartIndex != 0)
                    {
                        if (FullName.IndexOf(Pair.Value.Name, StartIndex) == StartIndex)
                        {
                            PrefixString = FullName.Substring(0, StartIndex);
                            return true;
                        }
                        return false;
                    }
                    return Pair.Value.Name.Equals(FullName);
                });
            Prefix = PrefixString;
            if (Player == null) { return -1; }
            return Player.SteamID;
        }

        public static long FindPlayerByControllerID(int ID)
        {
            var Player = Squad.Server.PlayersOnServer
                .Join(Squad.Players,
                    SteamID => SteamID,
                    Players => Players.Key,
                    (SteamID, Players) => new { SteamID, Players.Value })
                .FirstOrDefault(Pair => Pair.Value.ControllerID == ID);
            if (Player == null) { return -1; }
            return Player.SteamID;
        }
    }
    public class Player
    {
        public long SteamID { get; set; }
        public string Prefix { get; set; }
        public string Name { get; set; }
        public int ControllerID { get; set; }
        public long RoleID { get; set; }
        public long WeaponID { get; set; }
        public long FactionID { get; set; }
        public long VehicleID { get; set; }

        public static bool PlayersAreSameTeam(long PlayerID, long OtherPlayerID)
        {
            return (Squad.Players[PlayerID].FactionID == Squad.Players[OtherPlayerID].FactionID);
        }
    }

    public class Match
    {
        public long MatchID { get; set; }
        public DateTime MatchStart { get; set; }
        public DateTime MatchEnd { get; set; }
        public long MatchDuration { get; set; }
        public bool Valid { get; set; }
        public long TeamOneID { get; set; }
        public long TeamTwoID { get; set; }
    }

    public class SquadType
    {
        public long ID { get; }
        public string Name { get; }

        public SquadType(string Name)
        {
            this.ID = Squad.GetCRC32OfInput(Name);
            this.Name = Name;
        }
    }

    public class Map : SquadType
    {
        public Map(string Name) : base(Name) {}
    }

    public class Faction : SquadType
    {
        public string Abbreviation { get; set; }
        public Faction(string Name) : base(Name) { }

        public static long GetFactionByAbbreviation(string Abbreviation)
        {
            var Best = Squad.Factions.Select(Pair => new
                {
                    Item = Pair,
                    Score = ((Faction)Pair.Value).Abbreviation.LevenshteinDistance(Abbreviation)
                })
                .Aggregate(
                    (agg, next) => next.Score < agg.Score ? next : agg
                );
            Debug.Assert(Best.Score == 0);
            return Best.Item.Key;
        }
    }

    public class Role : SquadType
    {
        public Role(string Name) : base(Name) { }
    }

    public class Weapon : SquadType
    {
        public Weapon(string Name) : base(Name) { }
    }

    public class Vehicle : SquadType
    {
        public Vehicle(string Name) : base(Name) { }
    }
}
