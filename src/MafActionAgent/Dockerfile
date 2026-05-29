FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY MAF-A2A-NVIDIA-NemoAgents.sln ./
COPY src/Shared/Shared.csproj src/Shared/
COPY src/MafActionAgent/MafActionAgent.csproj src/MafActionAgent/

RUN dotnet restore src/MafActionAgent/MafActionAgent.csproj

COPY src/Shared src/Shared
COPY src/MafActionAgent src/MafActionAgent

RUN dotnet publish src/MafActionAgent/MafActionAgent.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5055 \
    MAF_HOST=0.0.0.0 \
    MAF_PORT=5055 \
    ENABLE_HEALTH_CHECKS=true \
    ENABLE_OTEL_TRACING=true

EXPOSE 5055

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MafActionAgent.dll"]
