Write-Host "Sn.ScreenBroadcaster Build Script"

New-Item -ItemType Directory -Force "./Builds" > $null
New-Item -ItemType Directory -Force "./Builds/Temp" > $null

dotnet build -c Release ./Sn.ScreenBroadcaster/Sn.ScreenBroadcaster.csproj

# Copy .NET8 Builds
Remove-Item -Force -Recurse "./Builds/Temp/*"
Copy-Item -Force -Recurse "./Sn.ScreenBroadcaster/bin/Release/net8.0-windows/*" "./Builds/Temp"
Remove-Item -Force -Recurse "./Builds/Temp/runtimes/osx"
Remove-Item -Force -Recurse "./Builds/Temp/runtimes/win-arm64"

# Compress .NET8 Full
Compress-Archive -Force "./Builds/Temp/*" "./Builds/Sn.ScreenBroadcaster-net8-full.zip"

Remove-Item -Force -Recurse "./Builds/Temp/runtimes/win-x86"

# Compress .NET8 X64
Compress-Archive -Force "./Builds/Temp/*" "./Builds/Sn.ScreenBroadcaster-net8-x64.zip"

# Copy .NET8 Builds
Remove-Item -Force -Recurse "./Builds/Temp/*"
Copy-Item -Force -Recurse "./Sn.ScreenBroadcaster/bin/Release/net8.0-windows/*" "./Builds/Temp"
Remove-Item -Force -Recurse "./Builds/Temp/runtimes/osx"
Remove-Item -Force -Recurse "./Builds/Temp/runtimes/win-arm64"
Remove-Item -Force -Recurse "./Builds/Temp/runtimes/win-x64"

# Compress .NET8 X64
Compress-Archive -Force "./Builds/Temp/*" "./Builds/Sn.ScreenBroadcaster-net8-x86.zip"

# Copy .NET481 Builds
Remove-Item -Force -Recurse "./Builds/Temp/*"
Copy-Item -Force -Recurse "./Sn.ScreenBroadcaster/bin/Release/net481/*" "./Builds/Temp"
Remove-Item -Force -Recurse "./Builds/Temp/arm64"
Remove-Item -Force -Recurse "./Builds/Temp/*.dylib"

# Compress .NET481 Full
Compress-Archive -Force "./Builds/Temp/*" "./Builds/Sn.ScreenBroadcaster-net481-full.zip"

Remove-Item -Force -Recurse "./Builds/Temp/x86"
Remove-Item -Force -Recurse "./Builds/Temp/dll/x86"

# Compress .NET481 x64
Compress-Archive -Force "./Builds/Temp/*" "./Builds/Sn.ScreenBroadcaster-net481-x64.zip"

# Copy .NET481 Builds
Remove-Item -Force -Recurse "./Builds/Temp/*"
Copy-Item -Force -Recurse "./Sn.ScreenBroadcaster/bin/Release/net481/*" "./Builds/Temp"
Remove-Item -Force -Recurse "./Builds/Temp/arm64"
Remove-Item -Force -Recurse "./Builds/Temp/x64"
Remove-Item -Force -Recurse "./Builds/Temp/dll/x64"
Remove-Item -Force -Recurse "./Builds/Temp/*.dylib"

# Compress .NET481 x86
Compress-Archive -Force "./Builds/Temp/*" "./Builds/Sn.ScreenBroadcaster-net481-x86.zip"

# Cleaning
Remove-Item -Force -Recurse "./Builds/Temp"