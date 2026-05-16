dotnet build DawnsburyArchipelago.csproj -c Release
Copy-Item -force "bin\x64\Release\net10.0-windows\DawnsburyArchipelago.dll" "CustomMods\DawnsburyArchipelago.dll"
Copy-Item -force "bin\x64\Release\net10.0-windows\Archipelago.MultiClient.Net.dll" "CustomMods\Archipelago.MultiClient.Net.dll"
Copy-Item -force "bin\x64\Release\net10.0-windows\0Harmony.dll" "CustomMods\0Harmony.dll"
Copy-Item -force "archipelago_logo.png" "CustomMods\archipelago_logo.png"
