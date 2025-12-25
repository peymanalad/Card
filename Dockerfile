FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY NuGet.config ./

COPY RayanparsiPackages ./RayanparsiPackages/

COPY Card/ ./Card/

WORKDIR /src/Card

RUN dotnet restore "Dario.Service.Card.API/Dario.Service.Card.API.csproj"

RUN dotnet publish "Dario.Service.Card.API/Dario.Service.Card.API.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

RUN apt-get update \
  && apt-get install -y --no-install-recommends curl \
  && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./


ENTRYPOINT ["dotnet", "Dario.Service.Card.API.dll"]
