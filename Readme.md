# Squad Stat Source
**A high performance log file data extraction tool for Squad Dedicated Servers.**
- Supports multiple dedicated servers running on the same machine.
- Supports reading backups - just place old log files in the dedicated server log folder (e.g \SquadGame\Saved\Logs\). 
- Failure tolerant - just picks back up from where it left off. 
- Reads the current log file live.
- Uses the cheapest possible operations to filter the logs before extracting data.
- Uses 0% CPU while running between parsing operations.

## Usage
1. Download the latest [Release](https://github.com/arukanoido/squad-stat-source/releases) or build from source. 
2. Edit `appsettings.json` and add paths to dedicated server folders.
Server entries must be in the format shown below. 
```
,{
  "id": "",             <-- Server's [Battlemetrics](https://www.battlemetrics.com/servers/search?game=squad) ID. 
  "path": "",           <-- Path to a Squad dedicated server program root directory (e.g C:\\Full\\Path\\To\\Server1)
  "lasttimestamp": ""   <-- As shown
}
```
Each `\` should be `\\`. Each set of `{}` should have `,` between them. See [JSON](https://jsonformatter.curiousconcept.com/) for more information.

Example:
```
  "servers": [
    {
      "id": "1234",
      "path": "C:\\Full\\Path\\To\\Server1",
      "lasttimestamp": ""
    }
    ,{
      "id": "5678",
      "path": "C:\\Full\\Path\\To\\Server2",
      "lasttimestamp": ""
    }
  ]
```
3. Run `SquadStatSource.exe`, and add it to your server startup script.

## Supported events:
- Match Start/End
- Player Join/Disconnect
- Player Spawned
- Player Changed Kit
- Player Entered/Exited Vehicle
- Player/Vehicle Damage
- Player Downed
- Player Give up
- Player Revive

Events not available in logs:
- Deployable digging with entrenching tool
- Player Healing
- Player shots fired vs shots hit

## Build for Windows

Run `build.bat`. 

## Build for Linux

## Libraries used
Data extraction: [Tokenizer](https://github.com/flipbit/tokenizer)
Name matching: [FuzzyString](https://github.com/kdjones/fuzzystring)
Serialization: [Json.NET](https://www.newtonsoft.com/json)