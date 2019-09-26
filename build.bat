@ECHO OFF

cd "C:\Users\Arkanoid\Desktop\squadstatsource\SquadStatSource"
dotnet publish -c Release -r win10-x64 --self-contained false -o "../Release"

cd ../Release
del *.dev.json
del *.pdb
del host*

