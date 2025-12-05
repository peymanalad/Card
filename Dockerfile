FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Card.sln ./
COPY NuGet.Config ./NuGet.Config
COPY RayanparsiPackages ./RayanparsiPackages

COPY Dario.Core.Abstraction.Card/Dario.Core.Abstraction.Card.csproj Dario.Core.Abstraction.Card/
COPY Dario.Core.Application.Card/Dario.Core.Application.Card.csproj Dario.Core.Application.Card/
COPY Dario.Core.Domain.Card/Dario.Core.Domain.Card.csproj Dario.Core.Domain.Card/
COPY Dario.Service.Card.API/Dario.Service.Card.API.csproj Dario.Service.Card.API/

RUN dotnet restore Card.sln --configfile ./NuGet.Config

COPY . .

RUN dotnet publish Dario.Service.Card.API/Dario.Service.Card.API.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:13276

COPY --from=build /app/publish .

EXPOSE 13276

ENTRYPOINT ["dotnet", "Dario.Service.Card.API.dll"]
