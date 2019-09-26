# Squad Stat Source
**A high performance log file data extraction tool for Squad Dedicated Servers.**
- Supports multiple dedicated servers running on the same machine. 
- Supports reading backups - just place old log files in the dedicated server log folder (e.g \SquadGame\Saved\Logs\). 
- Failure tolerant - just picks back up from where it left off. 
- Configurable run interval - default 1 minute.
- Uses the cheapest possible string operations to filter the logs by category before parsing them to extract data.
- Uses 0 CPU resources while running between processing operations.

## Usage
1. Download the latest Release or build from source. 
2. Edit appsettings.json and add paths to dedicated server folders.
Paths must be in the format shown below. 
```
,{
  "path": "C:\\Full\\Path\\To\\Server"
}
```
Each `\` should be `\\`. Each set of `{}` should have `,` between them. See [JSON](https://jsonformatter.curiousconcept.com/) for more information.

Example:
```
  "servers": [
    {
      "path": "C:\\Full\\Path\\To\\Server1"
    }
    ,{
      "path": "C:\\Full\\Path\\To\\Server2"
    }
  ]
```
3. Run it, and add it to your server startup script.

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

## Build for Linux

## Libraries used
Data extraction: [Tokenizer](https://github.com/flipbit/tokenizer)
Name matching: [FuzzyString](https://github.com/kdjones/fuzzystring)
Serialization: [Json.NET](https://www.newtonsoft.com/json)