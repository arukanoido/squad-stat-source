using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Parquet;
using Parquet.Data;
using Parquet.Data.Rows;

namespace SquadStatSourceWorker
{
    public class Serializer
    {
        public Table Table;

        public Serializer()
        {
            Schema Schema = new Schema(Events.Fields);
            Table = new Table(Schema);
        }

        public void Serialize()
        {
            if (Squad.Server.CurrentMatch.Valid)
            {
                string Base = "matchdata\\";
#if DEBUG
                Base = "..\\match\\";
#endif
#if RELEASE
                System.IO.Directory.CreateDirectory("matchdata");
#endif
                var Filename = Squad.Server.CurrentMatch.MatchID.ToString() + ".parquet";
                using (Stream FileStream = System.IO.File.Create(Base + Filename))
                {
                    using (ParquetWriter Writer = new ParquetWriter(Table.Schema, FileStream) { CompressionMethod = CompressionMethod.Snappy })
                    {
                        foreach(var Contract in Events.Contracts)
                        {
                            Contract.Resolve();
                        }

                        foreach (var Group in Events.FieldGroups)
                        {
                            foreach (var Row in Group.Rows)
                            {
                                Table.Add(Row);
                            }
                            if (Table.Count == 0) { continue; }
                        }
                        Writer.Write(Table);
                    }
                }
                Worker.Uploader.UploadDiff(Base);
            }
            Table.Clear();
            Clear();
            Squad.Server.CurrentMatch = null;
        }

        public static void Clear()
        {
            Events.Contracts.Clear();
            foreach (var Group in Events.FieldGroups)
            {
                Group.Rows.Clear();
            }
        }
    }

    /*
     * A group of columns used by an event
     */
    public class FieldGroup
    {
        List<int> Indices = new List<int>();
        public List<Row> Rows = new List<Row>();

        public FieldGroup(DataField[] NewFields)
        {
            foreach (var NewField in NewFields)
            {
                int Index = 0;
                if ((Index = FindIn(NewField, Events.Fields)) != -1)
                {
                    Indices.Add(Index);
                    continue; // Skip this one
                }
                Indices.Add(Events.Fields.Count);
                Events.Fields.Add(NewField);
            }
            Events.FieldGroups.Add(this);
        }

        public void AddRow(object[] Data, bool Optional = true)
        {
            Debug.Assert(Data.Length == Indices.Count); // Ensure you have 1 Data item per field
            List<Contract> Contracts = new List<Contract>();
            if (!Optional) { Squad.Server.CurrentMatch.Valid = true; }
            List<object> RowContents = new List<object>();
            for (var Index = 0; Index < Events.Fields.Count; Index++)
            {
                int Position = 0;
                if (Events.Fields[Index].Name.Equals("server_id", StringComparison.Ordinal))
                {
                    RowContents.Add(Squad.Server.ServerID);
                }
                else if (Events.Fields[Index].Name.Equals("match_id", StringComparison.Ordinal))
                {
                    RowContents.Add(Squad.Server.CurrentMatch.MatchID);
                }
                else if ((Position = Indices.IndexOf(Index)) != -1)
                {
                    RowContents.Add(Data[Position]);
                    if (Data[Position] is Contract)
                    {
                        var Contract  = (Contract)Data[Position];
                        Contract.Index = Index;
                        Contracts.Add(Contract);
                    }
                }
                else
                {
                    RowContents.Add(null);
                }
            }
            var Row = new Row(RowContents);
            Rows.Add(Row);
            foreach (var Contract in Contracts)
            {
                Contract.Target = Row.Values;
                Events.Contracts.Add(Contract);
            }
        }

        public int FindIn(DataField Field, List<DataField> Collection)
        {
            for (var Index = 0; Index < Collection.Count; Index++)
            {
                if (Collection[Index].Name.Equals(Field.Name, StringComparison.Ordinal))
                {
                    return Index;
                }
            }
            return -1;
        }
    }

    /* 
     * A data field that is populated with the correct value by the time we reach serialization
     * but is not guaranteed to be correct at the time we create the data in an event parse
     */
    public class Contract
    {
        Dictionary<long, SquadType> Collection { get; set; }
        object Potential { get; set; }
        public object[] Target { get; set; }
        public int Index { get; set; }

        public Contract(long Potential)
        {
            this.Potential = Potential;
        }

        public Contract(string Potential, Dictionary<long, SquadType> Collection)
        {
            this.Collection = Collection;
            this.Potential = Potential;
        }

        public void Resolve()
        {
            if (Potential is string)
            {
                var Name = ((string)Potential).Replace('_', ' ');
                var ID = Squad.GetItemOrDefault(Name, Collection, true);
                Target[Index] = ID;
            }
            else if (Potential is long)
            {
                Target[Index] = Potential;
            }
        }
    }

    public static class Events
    {
        public static List<DataField> Fields = new List<DataField>();

        public static List<Contract> Contracts = new List<Contract>();

        public static List<FieldGroup> FieldGroups = new List<FieldGroup>();

        public static FieldGroup BaseGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("server_id", DataType.Int64),
            new DataField("match_id", DataType.Int64)
        });

        public static Event[] List { get; } = {
            new PlayerJoined(),
            new PlayerJoinedDuringMapChange(),
            new PlayerDisconnected(),
            new PlayerKicked(),
            new PlayerRegisterClientAfterMapChange(),
            new PlayerSpawnOrSelectKit(),
            new PlayerEnterOrExitVehicle(),
            new PlayerDamageOrWound(),
            new PlayerBledOut(),
            new PlayerGaveUp(),
            new PlayerRevived(),
            new VehicleDamaged(),
            new MatchStart(),
            new MatchEnd()
        };

        private static Event ServerClosed = new ServerClosed();

        public static Serializer Serializer { get; } = new Serializer();
    }

#region --BaseEvent--

    public abstract class Event
    {
        public abstract string[] Category { get; }
        public virtual string Contains { get; } = null;
        public virtual bool WithinEventBoundary { get; } = false;
        public abstract string Pattern { get; }
        public DateTime Timestamp { get; set; }

        public abstract void Parse(string Line);
    }

#endregion

#region --PlayerJoined--

    public class PlayerJoined : Event
    {
        public override string[] Category { get; } = new string[] {
            "LogSquad: PostLogin: NewPlayer:",
            "LogEasyAntiCheatServer: [RegisterClient] Client:",
            "LogNet: Join succeeded:"
        };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquad: PostLogin: NewPlayer: BP_PlayerController_C {Controller$!:SubstringAfter('_C_')}
[{}][{}]LogEasyAntiCheatServer: [RegisterClient] Client: {} PlayerGUID: {SteamID!} PlayerIP: {} OwnerGUID: {} PlayerName: {$}
[{}][{}]LogNet: Join succeeded: {NameUTF8$!}";

        public string Controller { get; set; }
        public string SteamID { get; set; }
        public string NameUTF8 { get; set; }

        public static FieldGroup PlayerJoinGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_joined", DataType.Boolean)
        });

        static PlayerJoined() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerJoined>(Pattern, Line);
            if (Tokenized.Success)
            {
                var ID = Convert.ToInt64(Tokenized.Value.SteamID);
                Squad.Server.PlayersOnServer.Add(ID);
                var Player = new Player()
                {
                    SteamID = ID,
                    Name = Tokenized.Value.NameUTF8,
                    ControllerID = Convert.ToInt32(Tokenized.Value.Controller)
                };
                Squad.Players[ID] = Player;
                PlayerJoinGroup.AddRow(new object[] { (DateTimeOffset)Tokenized.Value.Timestamp, ID, true });
            }
        }
    }

#endregion

#region --PlayerJoinedDuringMapChange--

    // If a player joins during map change their steamID will not be registered, so we need to cache the player for later
    public class PlayerJoinedDuringMapChange : Event
    {
        public override string[] Category { get; } = new string[] {
            "LogSquad: Error: No teams exist yet, returning nullptr",
            "LogNet: Join succeeded:"
        };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquad: Error: No teams exist yet, returning nullptr in ChooseTeam for BP_PlayerController_C {Controller$!:SubstringAfter('_C_')} Name: {$}
[{}][{}]LogNet: Join succeeded: {NameUTF8$}";

        public string Controller { get; set; }
        public string NameUTF8 { get; set; }

        public static List<Player> List = new List<Player>();

        static PlayerJoinedDuringMapChange() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerJoinedDuringMapChange>(Pattern, Line);
            if (Tokenized.Success)
            {
                var Player = new Player()
                {
                    Name = Tokenized.Value.NameUTF8,
                    ControllerID = Convert.ToInt32(Tokenized.Value.Controller)
                };
                List.Add(Player);
            }
        }
    }

#endregion

#region --PlayerDisconnected--

    public class PlayerDisconnected : Event
    {
        public override string[] Category { get; } = new string[] { "LogNet: UNetConnection::Close: [UNetConnection]" };
        public override string Pattern { get; } = 
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogNet: UNetConnection::Close: [UNetConnection] RemoteAddr: {SteamID!:SubstringBefore(':')}, Name";
        public string SteamID { get; set; }

        public static FieldGroup PlayerDisconnectGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_disconnected", DataType.Boolean)
        });

        static PlayerDisconnected() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerDisconnected>(Pattern, Line);
            if (Tokenized.Success)
            {
                var ID = Convert.ToInt64(Tokenized.Value.SteamID);

                if (Squad.Server.PlayersOnServer.Contains(ID))
                {
                    Squad.Server.PlayersOnServer.Remove(ID);
                    PlayerDisconnectGroup.AddRow(new object[] { (DateTimeOffset)Tokenized.Value.Timestamp, ID, true });
                }
            }
        }
    }

    #endregion

    #region --PlayerKicked--

    public class PlayerKicked : Event
    {
        public override string[] Category { get; } = new string[] { "LogOnlineGame: Display: Kicking player:" };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogOnlineGame: Display: Kicking player: {PlayerNameWithPrefixUTF8} ; Reason";
        public string PlayerNameWithPrefixUTF8 { get; set; }

        public static FieldGroup PlayerKickedGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_disconnected", DataType.Boolean)
        });

        static PlayerKicked() { }

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerKicked>(Pattern, Line);
            if (Tokenized.Success)
            {
                string PlayerPrefix = null;
                long ID = Server.FindPlayerByFullName(Tokenized.Value.PlayerNameWithPrefixUTF8, out PlayerPrefix);
                Squad.Players[ID].Prefix = PlayerPrefix;
                Debug.Assert(ID != -1);

                if (Squad.Server.PlayersOnServer.Contains(ID))
                {
                    Squad.Server.PlayersOnServer.Remove(ID);
                    PlayerKickedGroup.AddRow(new object[] { (DateTimeOffset)Tokenized.Value.Timestamp, ID, true });
                }
            }
        }
    }

    #endregion

    #region --PlayerRegisterClientAfterMapChange--

    // Works with PlayerJoinDuringMapChange. Only handles the edge case.
    public class PlayerRegisterClientAfterMapChange : Event
    {
        public override string[] Category { get; } = new string[] { "LogEasyAntiCheatServer: [RegisterClient] Client:" };
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogEasyAntiCheatServer: [RegisterClient] Client: {EngineID} PlayerGUID: {SteamID} PlayerIP: {} OwnerGUID: {} PlayerName: {$}";
        public string SteamID { get; set; }

        static PlayerRegisterClientAfterMapChange() {}

        public override void Parse(string Line)
        {
            if (PlayerJoinedDuringMapChange.List.Count > 0)
            {
                var Tokenized = Worker.Tokenizer.Tokenize<PlayerRegisterClientAfterMapChange>(Pattern, Line);
                if (Tokenized.Success)
                {
                    long ID = Convert.ToInt64(Tokenized.Value.SteamID);
                    if (!Squad.Server.PlayersOnServer.Contains(ID))
                    {
                        Squad.Server.PlayersOnServer.Add(ID);
                        var Player = PlayerJoinedDuringMapChange.List[0];
                        Player.SteamID = ID;
                        Squad.Players[ID] = Player;
                        PlayerJoinedDuringMapChange.List.RemoveAt(0);

                        PlayerJoined.PlayerJoinGroup.AddRow(new object[] { (DateTimeOffset)Tokenized.Value.Timestamp, ID, true });
                    }
                }
            }
        }
    }

#endregion

#region --PlayerSpawnOrSelectKit--

    // Matches when player sets a new role e.g by spawning or at ammo crate or vehicle
    // If the role was set while the player was already spawned, Pawn will be null
    public class PlayerSpawnOrSelectKit : Event
    {
        public override string[] Category { get; } = new string[] {
            "LogSquadTrace: [DedicatedServer]ASQPlayerController::SetCurrentRole(): On Server PC",
            "LogSquadTrace: [DedicatedServer]ASQPlayerController::SetCurrentRole(): On Server PC",
            "LogSquadTrace: [DedicatedServer]ASQPlayerController::Possess():"
        };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquadTrace: [DedicatedServer]ASQPlayerController::SetCurrentRole(): On Server PC={NameUTF8!} NewRole={KitName$!:NotEqual('nullptr')}
[{}][{}]LogSquadTrace: [DedicatedServer]ASQPlayerController::SetCurrentRole(): On Server PC={} CurrentRole={$}
[{}][{}]LogSquadTrace: [DedicatedServer]ASQPlayerController::Possess(): PC={PossessNameUTF8} Pawn={Pawn} FullPath";
        public override bool WithinEventBoundary { get; } = true;

        public string NameUTF8 { get; set; }
        public string KitName { get; set; }
        public string Pawn { get; set; }
        public string PossessNameUTF8 { get; set; }

        public static FieldGroup PlayerSpawnedGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_spawned", DataType.Boolean),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64)
        });

        public static FieldGroup PlayerChangedKitGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_changed_kit", DataType.Boolean),
            new DataField("role_id", DataType.Int64)
        });

        static PlayerSpawnOrSelectKit() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerSpawnOrSelectKit>(Pattern, Line);
            if (Tokenized.Success)
            {
                var SteamID = Server.FindPlayerByName(Tokenized.Value.NameUTF8);
                Debug.Assert(SteamID != -1);

                var RoleID = Squad.GetItemOrDefault(Tokenized.Value.KitName, Squad.Roles);
                Squad.Players[SteamID].RoleID = RoleID;

                // Handles a rare case where one player changed kit and another player possessed a vehicle
                // in the same event ID
                bool ChangedKitEvent = false;
                if (Tokenized.Value.PossessNameUTF8 != null && 
                    !Tokenized.Value.NameUTF8.Equals(Tokenized.Value.PossessNameUTF8))
                {
                    var EventLine = Regex.Split(Line, "\r\n|\r|\n");
                    var Event = new PlayerEnterOrExitVehicle();
                    Event.Parse(EventLine[2]);
                    ChangedKitEvent = true;
                }

                // Spawned in
                if (Tokenized.Value.Pawn != null && !ChangedKitEvent)
                {
                    var Parts = Tokenized.Value.Pawn.Split('_');
                    var FactionID = Faction.GetFactionByAbbreviation(Parts[2]);
                    Squad.Players[SteamID].FactionID = FactionID;

                    if (Squad.Server.CurrentMatch.TeamOneID == 0L)
                    {
                        Squad.Server.CurrentMatch.TeamOneID = FactionID;
                    }
                    else if (FactionID != Squad.Server.CurrentMatch.TeamOneID)
                    {
                        Squad.Server.CurrentMatch.TeamTwoID = FactionID;
                    }
                    PlayerSpawnedGroup.AddRow(new object[] { 
                        (DateTimeOffset)Tokenized.Value.Timestamp, 
                        SteamID, true, RoleID, FactionID
                    });
                }
                // Changed kit at ammo crate
                else
                {
                    PlayerChangedKitGroup.AddRow(new object[] { 
                        (DateTimeOffset)Tokenized.Value.Timestamp, 
                        SteamID, true, RoleID
                    });
                }
            }
        }
    }

#endregion

#region --PlayerEnterOrExitVehicle--

    public class PlayerEnterOrExitVehicle : Event
    {
        public override string[] Category { get; } = new string[] { "LogSquadTrace: [DedicatedServer]ASQPlayerController::Possess():" };
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquadTrace: [DedicatedServer]ASQPlayerController::Possess(): PC={NameUTF8!} Pawn={} FullPath={Pawn!$} /{$}";

        public string NameUTF8 { get; set; }
        public string Pawn { get; set; }

        public static FieldGroup PlayerEnterVehicleGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_entered_vehicle", DataType.Boolean),
            new DataField("vehicle_id", DataType.Int64)
        });

        public static FieldGroup PlayerExitVehicleGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("player_exited_vehicle", DataType.Boolean)
        });

        static PlayerEnterOrExitVehicle() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerEnterOrExitVehicle>(Pattern, Line);
            if (Tokenized.Success)
            {
                var SteamID = Server.FindPlayerByName(Tokenized.Value.NameUTF8);
                /* 
                 * If SteamID is -1 (not found) the player has disconnected while in a vehicle. After the player leaves
                 * the server, the player's Controller exits the vehicle before getting cleaned up.
                 */
                if (SteamID != -1) {
                    if (Tokenized.Value.Pawn.StartsWith("BP_Soldier_"))
                    {
                        // Exited vehicle
                        if (Squad.Players[SteamID].VehicleID != 0)
                        {
                            Squad.Players[SteamID].VehicleID = 0;
                            PlayerExitVehicleGroup.AddRow(new object[] {
                                (DateTimeOffset)Tokenized.Value.Timestamp, SteamID, true
                            });
                        }
                    }
                    else
                    {
                        var PawnName = Squad.TrimIDFromName(Tokenized.Value.Pawn);
                        if (Tokenized.Value.Pawn.StartsWith("SQDeployable"))
                        {
                            // @todo emplacement
                        }
                        else
                        {
                            // Entered vehicle
                            //var VehicleID = Squad.GetExactMatch(PawnName, Squad.Vehicles);
                            var VehicleID = Squad.GetItemOrDefault(PawnName, Squad.Vehicles);
                            Squad.Players[SteamID].VehicleID = VehicleID;

                            PlayerEnterVehicleGroup.AddRow(new object[] {
                                (DateTimeOffset)Tokenized.Value.Timestamp, SteamID, true, VehicleID
                            });

                            // @todo track vehicle seats based on who has entered/exited the vehicle
                            /*if (VehicleID == -1)
                            {
                                // @todo turret gunner

                                // instead of recording the vehicle ID as -1, switch to GetItemOrDefault
                                // to ensure that any vehicles added in future get properly registered
                            }
                            else 
                            {
                                // A supported vehicle
                                PlayerEnterVehicleGroup.AddRow(new object[] {
                                    (DateTimeOffset)Tokenized.Value.Timestamp, SteamID, true, VehicleID
                                });
                            }*/
                        }
                    }
                }
            }
        }
    }

#endregion

#region --PlayerDamageOrWound--

    /*
     * Check the special case of a player being damaged inside a vehicle first
     * If player was damaged in a vehicle the name will be nullptr
     * In addition, if the player died in a vehicle they will possess their soldier in the same tick, so we check that here
     */
    public class PlayerDamageOrWound : Event
    {
        public override string[] Category { get; } = new string[] {
            "LogSquad: Player:",
            "LogSquadTrace: [DedicatedServer]ASQSoldier::Wound():",
            "LogSquadTrace: [DedicatedServer]ASQPlayerController::Possess():"
        };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquad: Player:{VictimNameUTF8WithPrefix!} ActualDamage={Damage!} from {CauserNameUTF8WithPrefix!:NotEqual('nullptr')} caused by {Weapon$!}
[{}][{}]LogSquadTrace: [DedicatedServer]ASQSoldier::Wound(): Player:{} KillingDamage={KillingDamage} from {CauserController:SubstringAfter('_C_')} caused by {$}
[{}][{}]LogSquadTrace: [DedicatedServer]ASQPlayerController::Possess(): PC={VictimNameUTF8NotNull} Pawn={VictimPawn} FullPath={$}";

        public string VictimNameUTF8WithPrefix { get; set; }
        public string Damage { get; set; }
        public string CauserNameUTF8WithPrefix { get; set; }
        public string Weapon { get; set; }
        public string KillingDamage { get; set; }
        public string CauserController { get; set; }
        public string VictimNameUTF8NotNull { get; set; }
        public string VictimPawn { get; set; }

        public static FieldGroup PlayerDamageGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("victim_id", DataType.Int64),
            new DataField("causer_id", DataType.Int64),
            new DataField("weapon_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("damage", DataType.Float),
            new DataField("downed", DataType.Boolean),
            new DataField("enemy_damage", DataType.Boolean)
        });

        public static FieldGroup PlayerTeamDamageGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("victim_id", DataType.Int64),
            new DataField("causer_id", DataType.Int64),
            new DataField("weapon_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("damage", DataType.Float),
            new DataField("downed", DataType.Boolean),
            new DataField("team_damage", DataType.Boolean)
        });

        public static FieldGroup PlayerSelfDamageGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("weapon_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("damage", DataType.Float),
            new DataField("downed", DataType.Boolean),
            new DataField("self_damage", DataType.Boolean)
        });

        static PlayerDamageOrWound() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerDamageOrWound>(Pattern, Line);
            if (Tokenized.Success)
            {
                float Damage = Convert.ToSingle(Tokenized.Value.Damage);
                float KillingDamage = Convert.ToSingle(Tokenized.Value.KillingDamage);
                bool Downed = false;
                long VictimSteamID = -1;

                string CauserPrefix = null;
                long CauserSteamID = Server.FindPlayerByFullName(Tokenized.Value.CauserNameUTF8WithPrefix, out CauserPrefix);
                Squad.Players[CauserSteamID].Prefix = CauserPrefix;
                Debug.Assert(CauserSteamID != -1);

                string WeaponName = Squad.TrimIDFromName(Tokenized.Value.Weapon);
                long WeaponID = Squad.GetItemOrDefault(WeaponName, Squad.Weapons);
                Squad.Players[CauserSteamID].WeaponID = WeaponID;

                if (Tokenized.Value.CauserController != null)
                {
                    if (KillingDamage == 0.0f)
                    {
                        // Player bled out, so send the particular line to PlayerBledOut for parsing
                        var  EventLine = Regex.Split(Line, "\r\n|\r|\n");
                        var Event = new PlayerBledOut();
                        Event.Parse(EventLine[1]);
                    }
                    else
                    {
                        Squad.Players[CauserSteamID].ControllerID = Convert.ToInt32(Tokenized.Value.CauserController);
                    }
                }
                
                // Case where player takes damage as a vehicle passenger, as opposed to the case where a vehicle takes damage
                if (Tokenized.Value.VictimNameUTF8WithPrefix.Equals("nullptr", StringComparison.Ordinal))
                {
                    // We can only track downed events for players in vehicles because damage-only events don't identify the
                    // player properly (as nullptr). As it is, we are required to use an additional Possess() event to handle the downed case.
                    Downed = (Tokenized.Value.CauserController == null) ? false : true;
                    // In rare cases, VictimNameUTF8NotNull is still null. There's no way to properly assign the event to players so we drop them.
                    if (Downed && Tokenized.Value.VictimNameUTF8NotNull != null)
                    {
                        VictimSteamID = Server.FindPlayerByName(Tokenized.Value.VictimNameUTF8NotNull);
                        Debug.Assert(VictimSteamID != -1);
                        if (Player.PlayersAreSameTeam(VictimSteamID, CauserSteamID))
                        {
                            PlayerTeamDamageGroup.AddRow(new object[] {
                                (DateTimeOffset)Tokenized.Value.Timestamp,
                                VictimSteamID, CauserSteamID,
                                Squad.Players[CauserSteamID].WeaponID,
                                Squad.Players[CauserSteamID].RoleID,
                                Squad.Players[CauserSteamID].FactionID,
                                Damage, Downed, true
                            });
                        }
                        else
                        {
                            PlayerDamageGroup.AddRow(new object[] {
                                (DateTimeOffset)Tokenized.Value.Timestamp,
                                VictimSteamID, CauserSteamID,
                                Squad.Players[CauserSteamID].WeaponID,
                                Squad.Players[CauserSteamID].RoleID,
                                Squad.Players[CauserSteamID].FactionID,
                                Damage, Downed, true
                            });
                        }
                    }
                }
                // Regular damage/downed events for soldiers on foot
                else
                {
                    string VictimPrefix = null;
                    VictimSteamID = Server.FindPlayerByFullName(Tokenized.Value.VictimNameUTF8WithPrefix, out VictimPrefix);
                    // If victim is not found, they disconnected while in combat
                    if (VictimSteamID == -1) { return; }
                    Squad.Players[VictimSteamID].Prefix = VictimPrefix;

                    // Damage event
                    if (Tokenized.Value.CauserController == null || KillingDamage == 0.0f)
                    {
                        Downed = false;
                        // Self damage
                        if (VictimSteamID == CauserSteamID)
                        {
                            PlayerSelfDamageGroup.AddRow(new object[] {
                                (DateTimeOffset)Tokenized.Value.Timestamp,
                                VictimSteamID,
                                Squad.Players[VictimSteamID].WeaponID,
                                Squad.Players[VictimSteamID].RoleID,
                                Squad.Players[VictimSteamID].FactionID, 
                                Damage, Downed, true
                            });
                        }
                        // Damaged by another
                        else
                        {
                            if (Player.PlayersAreSameTeam(VictimSteamID, CauserSteamID))
                            {
                                PlayerTeamDamageGroup.AddRow(new object[] {
                                    (DateTimeOffset)Tokenized.Value.Timestamp,
                                    VictimSteamID, CauserSteamID,
                                    Squad.Players[CauserSteamID].WeaponID,
                                    Squad.Players[CauserSteamID].RoleID,
                                    Squad.Players[CauserSteamID].FactionID,
                                    Damage, Downed, true
                                });
                            }
                            else
                            {
                                PlayerDamageGroup.AddRow(new object[] {
                                    (DateTimeOffset)Tokenized.Value.Timestamp,
                                    VictimSteamID, CauserSteamID,
                                    Squad.Players[CauserSteamID].WeaponID,
                                    Squad.Players[CauserSteamID].RoleID,
                                    Squad.Players[CauserSteamID].FactionID,
                                    Damage, Downed, true
                                });
                            }
                        }
                    }
                    // Downed event
                    else
                    {
                        Downed = true;
                        // Player downed themselves
                        if (VictimSteamID == CauserSteamID)
                        {
                            PlayerSelfDamageGroup.AddRow(new object[] {
                                (DateTimeOffset)Tokenized.Value.Timestamp,
                                VictimSteamID,
                                Squad.Players[VictimSteamID].WeaponID,
                                Squad.Players[VictimSteamID].RoleID,
                                Squad.Players[VictimSteamID].FactionID,
                                Damage, Downed, true
                            });
                        }
                        // Downed by another player
                        else
                        {
                            if (Player.PlayersAreSameTeam(VictimSteamID, CauserSteamID))
                            {
                                PlayerTeamDamageGroup.AddRow(new object[] {
                                    (DateTimeOffset)Tokenized.Value.Timestamp,
                                    VictimSteamID, CauserSteamID,
                                    Squad.Players[CauserSteamID].WeaponID,
                                    Squad.Players[CauserSteamID].RoleID,
                                    Squad.Players[CauserSteamID].FactionID,
                                    Damage, Downed, true
                                });
                            }
                            else
                            {
                                PlayerDamageGroup.AddRow(new object[] {
                                    (DateTimeOffset)Tokenized.Value.Timestamp,
                                    VictimSteamID, CauserSteamID,
                                    Squad.Players[CauserSteamID].WeaponID,
                                    Squad.Players[CauserSteamID].RoleID,
                                    Squad.Players[CauserSteamID].FactionID,
                                    Damage, Downed, true
                                }, false);
                            }
                        }
                    }
                }
            }
        }
    }

#endregion

#region --PlayerBledOut--

    // When a player bleeds out, killing damage is listed as 0.0
    public class PlayerBledOut : Event
    {
        public override string[] Category { get; } = new string[] { "LogSquadTrace: [DedicatedServer]ASQSoldier::Wound():" };
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquadTrace: [DedicatedServer]ASQSoldier::Wound(): Player:{VictimNameUTF8WithPrefix!} KillingDamage={} from {CauserController!:NotEqual('nullptr'),SubstringAfter('_C_')} caused by {$}";
        
        public string VictimNameUTF8WithPrefix { get; set; }
        public string CauserController { get; set; }

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerBledOut>(Pattern, Line);
            if (Tokenized.Success)
            {
                string Prefix = null;
                long VictimSteamID = Server.FindPlayerByFullName(Tokenized.Value.VictimNameUTF8WithPrefix, out Prefix);
                if (VictimSteamID == -1) { return; }
                Squad.Players[VictimSteamID].Prefix = Prefix;

                long CauserSteamID = Server.FindPlayerByControllerID(Convert.ToInt32(Tokenized.Value.CauserController));

                // Bled out from self damage
                if (VictimSteamID == CauserSteamID)
                {
                    PlayerDamageOrWound.PlayerSelfDamageGroup.AddRow(new object[] {
                        (DateTimeOffset)Tokenized.Value.Timestamp,
                        VictimSteamID,
                        Squad.Players[VictimSteamID].WeaponID,
                        Squad.Players[VictimSteamID].RoleID,
                        Squad.Players[VictimSteamID].FactionID,
                        0.0f, true, true
                    });
                }
                // Bled out from damage by another player
                else
                {
                    if (Player.PlayersAreSameTeam(VictimSteamID, CauserSteamID))
                    {
                        PlayerDamageOrWound.PlayerTeamDamageGroup.AddRow(new object[] {
                            (DateTimeOffset)Tokenized.Value.Timestamp,
                            VictimSteamID, CauserSteamID,
                            Squad.Players[CauserSteamID].WeaponID,
                            Squad.Players[CauserSteamID].RoleID,
                            Squad.Players[CauserSteamID].FactionID,
                            0.0f, true, true
                        });
                    }
                    else
                    {
                        PlayerDamageOrWound.PlayerDamageGroup.AddRow(new object[] {
                            (DateTimeOffset)Tokenized.Value.Timestamp,
                            VictimSteamID, CauserSteamID,
                            Squad.Players[CauserSteamID].WeaponID,
                            Squad.Players[CauserSteamID].RoleID,
                            Squad.Players[CauserSteamID].FactionID,
                            0.0f, true, true
                        });
                    }
                }
            }
        }
    }

#endregion

#region --PlayerGaveUp--

    public class PlayerGaveUp : Event
    {
        public override string[] Category { get; } = new string[] { "LogSquadTrace: [DedicatedServer]ASQSoldier::Die():" };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquadTrace: [DedicatedServer]ASQSoldier::Die(): Player:{VictimNameUTF8WithPrefix!:NotEqual('nullptr')} KillingDamage={} from {CauserController!:SubstringAfter('_C_')} caused by {CauserPawn$!}";

        public string VictimNameUTF8WithPrefix { get; set; }
        public string CauserController { get; set; }
        public string CauserPawn { get; set; }

        public static FieldGroup PlayerKillGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("victim_id", DataType.Int64),
            new DataField("causer_id", DataType.Int64),
            new DataField("weapon_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("kill", DataType.Boolean)
        });

        public static FieldGroup PlayerTeamKillGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("victim_id", DataType.Int64),
            new DataField("causer_id", DataType.Int64),
            new DataField("weapon_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("teamkill", DataType.Boolean)
        });

        public static FieldGroup PlayerSuicideGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("player_id", DataType.Int64),
            new DataField("weapon_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("suicide", DataType.Boolean)
        });

        static PlayerGaveUp() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerGaveUp>(Pattern, Line);
            if (Tokenized.Success)
            {
                string VictimPrefix = null;
                long VictimSteamID = Server.FindPlayerByFullName(Tokenized.Value.VictimNameUTF8WithPrefix, out VictimPrefix);
                Squad.Players[VictimSteamID].Prefix = VictimPrefix;
                Debug.Assert(VictimSteamID != -1);

                // Toaster bath
                if (Tokenized.Value.CauserPawn.Equals("nullptr", StringComparison.Ordinal))
                {
                    PlayerSuicideGroup.AddRow(new object[] {
                        (DateTimeOffset)Tokenized.Value.Timestamp,
                        VictimSteamID,
                        Squad.Players[VictimSteamID].WeaponID,
                        Squad.Players[VictimSteamID].RoleID,
                        Squad.Players[VictimSteamID].FactionID,
                        true
                    });
                }
                // Died to another player
                else
                {
                    // Player left the server and cannot be credited with the kill
                    if (Tokenized.Value.CauserController.Equals("nullptr", StringComparison.Ordinal)) { return; }

                    long CauserSteamID = Server.FindPlayerByControllerID(Convert.ToInt32(Tokenized.Value.CauserController));
                    Debug.Assert(CauserSteamID != -1);

                    if (Player.PlayersAreSameTeam(VictimSteamID, CauserSteamID))
                    {
                        PlayerTeamKillGroup.AddRow(new object[] {
                            (DateTimeOffset)Tokenized.Value.Timestamp,
                            VictimSteamID, CauserSteamID,
                            Squad.Players[CauserSteamID].WeaponID,
                            Squad.Players[CauserSteamID].RoleID,
                            Squad.Players[CauserSteamID].FactionID,
                            true
                        });
                    }
                    else
                    {
                        PlayerKillGroup.AddRow(new object[] {
                            (DateTimeOffset)Tokenized.Value.Timestamp,
                            VictimSteamID, CauserSteamID,
                            Squad.Players[CauserSteamID].WeaponID,
                            Squad.Players[CauserSteamID].RoleID,
                            Squad.Players[CauserSteamID].FactionID,
                            true
                        }, false);
                    }
                }
            }
        }
    }

#endregion

#region --PlayerRevived--

    public class PlayerRevived : Event
    {
        public override string[] Category { get; } = new string[] { "LogSquad:" };
        // The log category isn't unique enough so we require a contains check
        public override string Contains { get; } = "has revived";
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquad: {ReviverNameUTF8WithPrefix!} has revived {RevivedNameUTF8WithPrefix$!:ChopEnd(1)}";
        public string ReviverNameUTF8WithPrefix { get; set; }
        public string RevivedNameUTF8WithPrefix { get; set; }

        public static FieldGroup PlayerReviveGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime),
            new DataField("causer_id", DataType.Int64),
            new DataField("victim_id", DataType.Int64),
            new DataField("role_id", DataType.Int64),
            new DataField("faction_id", DataType.Int64),
            new DataField("revive", DataType.Boolean)
        });

        static PlayerRevived() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<PlayerRevived>(Pattern, Line);
            if (Tokenized.Success)
            {
                string ReviverPrefix = null;
                long ReviverSteamID = Server.FindPlayerByFullName(Tokenized.Value.ReviverNameUTF8WithPrefix, out ReviverPrefix);
                Squad.Players[ReviverSteamID].Prefix = ReviverPrefix;
                Debug.Assert(ReviverSteamID != -1);

                string RevivedPrefix = null;
                long RevivedSteamID = Server.FindPlayerByFullName(Tokenized.Value.RevivedNameUTF8WithPrefix, out RevivedPrefix);
                Squad.Players[RevivedSteamID].Prefix = RevivedPrefix;
                Debug.Assert(RevivedSteamID != -1);

                PlayerReviveGroup.AddRow(new object[] {
                    (DateTimeOffset)Tokenized.Value.Timestamp,
                    ReviverSteamID, RevivedSteamID,
                    Squad.Players[ReviverSteamID].RoleID,
                    Squad.Players[ReviverSteamID].FactionID,
                    true
                });
            }
        }
    }

#endregion

#region --VehicleDamaged--

    public class VehicleDamaged : Event
    {
        public override string[] Category { get; } = new string[] { "LogSquadTrace: [DedicatedServer]ASQVehicle::TakeDamage():" };
        public override string Pattern { get; } =
@"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquadTrace: [DedicatedServer]ASQVehicle::TakeDamage(): {VictimName!}: {Damage!} damage taken by causer {Causer!} instigator {Instigator!:NotEqual('nullptr')} health remaining {HealthLeft$!}";

        public string VictimName { get; set; }
        public string Damage { get; set; }
        public string Causer { get; set; }
        public string Instigator { get; set; }
        public string HealthLeft { get; set; }

        static VehicleDamaged() {}

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<VehicleDamaged>(Pattern, Line);
            if (Tokenized.Success)
            {
                // @todo vehicles, vehicle seats, vehicle turrets, vehicle weapons
                // if a player enters a vehicle with a driver already in it,
                // they possess the turret right away, otherwise they possess the turret later
                // when a vehicle does damage, the vehicle weapon is listed in the damage event
                if (Convert.ToSingle(Tokenized.Value.HealthLeft) <= 0.0f)
                {
                    // Vehicle destroyed
                    //Console.WriteLine(Tokenized.Value.VictimName + " destroyed by " + Tokenized.Value.Instigator);
                }
                else
                {
                    // Vehicle damaged
                    //Console.WriteLine(Tokenized.Value.VictimName + " by " + Tokenized.Value.Instigator);

                    // If Victim Name is a vehicle instead of a player, the vehicle was empty
                    // MAYBE, needs testing
                }
            }
        }
    }

#endregion

#region --MatchStart--

    public class MatchStart : Event
    {
        public override string[] Category { get; } = new string[] { "LogWorld: Bringing World" };
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogWorld: Bringing World {Map!:SubstringAfter('.'),NotEqual('TransitionMap')} up for play";
        public string Map { get; set; }

        public static FieldGroup MatchStartGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime, false),
            new DataField("match_started", DataType.Boolean)
        });

        static MatchStart() { }

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<MatchStart>(Pattern, Line);
            if (Tokenized.Success)
            {
                if (Squad.Server.CurrentMatch != null)
                {
                    // Write a completed match to file
                    Events.Serializer.Serialize();
                }

                Squad.Server.CurrentMatch = new Match()
                {
                    MatchStart = Tokenized.Value.Timestamp,
                };
                Squad.Server.CurrentMatch.MatchID = Squad.GetCRC32OfInput(
                    Squad.Server.ServerID.ToString() +
                    Squad.Server.CurrentMatch.MatchStart.ToString()
                );
                MatchStartGroup.AddRow(new object[] { (DateTimeOffset)Tokenized.Value.Timestamp, true });
            }
        }
    }

#endregion

#region --MatchEnd--

    public class MatchEnd : Event
    {
        public override string[] Category { get; } = new string[] { "LogSquadTrace: [DedicatedServer]ASQGameMode::DetermineMatchWinner():" };
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]LogSquadTrace: [DedicatedServer]ASQGameMode::DetermineMatchWinner(): {Faction!} won on {Map!}";
        public string Faction { get; set; }
        public string Map { get; set; }

        public static FieldGroup MatchEndGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime, false),
            new DataField("match_ended", DataType.Boolean),
            new DataField("winner_faction_id", DataType.Int64),
            new DataField("faction_one_id", DataType.Int64),
            new DataField("faction_two_id", DataType.Int64),
            new DataField("map_id", DataType.Int64)
        });

        static MatchEnd() { }

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<MatchEnd>(Pattern, Line);
            if (Tokenized.Success)
            {
                var CurrentMatch = Squad.Server.CurrentMatch;
                CurrentMatch.MatchEnd = Tokenized.Value.Timestamp;
                CurrentMatch.MatchDuration = (CurrentMatch.MatchEnd - CurrentMatch.MatchStart).Ticks;

                var Map = new SquadType(Tokenized.Value.Map);
                Squad.Maps.TryAdd(Map.ID, Map);

                var FactionID = Squad.GetItemOrDefault(Tokenized.Value.Faction, Squad.Factions);

                MatchEndGroup.AddRow(new object[] { 
                    (DateTimeOffset)Tokenized.Value.Timestamp, 
                    true, 
                    FactionID,
                    new Contract(Squad.Server.CurrentMatch.TeamOneID),
                    new Contract(Squad.Server.CurrentMatch.TeamTwoID),
                    Map.ID
                });

                Worker.WriteDedicatedServerConfig("lasttimestamp", Line.Substring(1, 23));
            }
        }
    }

#endregion

#region --ServerClosed--

    // This event is inferred from encountering the end of a log file
    public class ServerClosed : Event
    {
        public override string[] Category { get; } = new string[] { "" };
        public override string Pattern { get; } = @"[{Timestamp:ToDateTime('yyyy.MM.dd-HH.mm.ss:fff')}][{}]{$}";

        public static FieldGroup ServerClosedGroup { get; } = new FieldGroup(new DataField[] {
            new DateTimeDataField("timestamp", DateTimeFormat.DateAndTime, false),
            new DataField("faction_one_id", DataType.Int64),
            new DataField("faction_two_id", DataType.Int64),
            new DataField("server_shutdown", DataType.Boolean)
        });

        static ServerClosed() { }

        public override void Parse(string Line)
        {
            var Tokenized = Worker.Tokenizer.Tokenize<ServerClosed>(Pattern, Line);
            if (Tokenized.Success)
            {
                ServerClosedGroup.AddRow(new object[] {
                    (DateTimeOffset)Tokenized.Value.Timestamp,
                    new Contract(Squad.Server.CurrentMatch.TeamOneID),
                    new Contract(Squad.Server.CurrentMatch.TeamTwoID),
                    true
                });

                Events.Serializer.Serialize();
                Worker.WriteDedicatedServerConfig("lasttimestamp", Line.Substring(1, 23));
                Squad.Server.PlayersOnServer.Clear();
            }
        }
    }

#endregion

}