FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AikoBot/AikoBot.csproj src/AikoBot/
RUN dotnet restore src/AikoBot/AikoBot.csproj

COPY . .
RUN dotnet publish src/AikoBot/AikoBot.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

RUN mkdir -p /app/data /app/logs /home/data /home/LogFiles

COPY --from=build /app/publish .

ENV DOTNET_EnableDiagnostics=0 \
    BotConfiguration__DatabasePath=/app/data/aiko-bot.db \
    BotConfiguration__SystemPromptFilePath=/app/system-prompt.txt \
    BotConfiguration__EnhancingPromptFilePath=/app/enhancing-prompt.txt

VOLUME ["/app/data", "/app/logs"]

ENTRYPOINT ["dotnet", "AikoBot.dll"]
