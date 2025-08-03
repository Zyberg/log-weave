dotnet build -c Release MethodLogger.Fody/MethodLogger.Fody.csproj

dotnet build -c Release MethodLogger/MethodLogger.csproj

rm -rf SmokeTest/bin SmokeTest/obj

dotnet nuget locals all --clear

dotnet run --project SmokeTest/SmokeTest.csproj
