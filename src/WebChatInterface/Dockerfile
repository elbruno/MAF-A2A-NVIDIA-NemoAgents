FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY MAF-A2A-NVIDIA-NemoAgents.sln ./
COPY src/Shared/Shared.csproj src/Shared/
COPY src/WebChatInterface/WebChatInterface.csproj src/WebChatInterface/

RUN dotnet restore src/WebChatInterface/WebChatInterface.csproj

COPY src/Shared src/Shared
COPY src/WebChatInterface src/WebChatInterface

RUN dotnet publish src/WebChatInterface/WebChatInterface.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5000 \
    WEB_UI_HOST=0.0.0.0 \
    WEB_UI_PORT=5000 \
    ENABLE_OTEL_TRACING=true \
    NEMO_WARMUP_ENABLED=false

EXPOSE 5000

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "WebChatInterface.dll"]
