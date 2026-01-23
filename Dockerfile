# Dockerfile otimizado para Rinha
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

# Instalação mínima para health check
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia o projeto primeiro (para cache de layers)
COPY ["rinhaBackend.csproj", "./"]
RUN dotnet restore "rinhaBackend.csproj"

# Copia o resto do código
COPY . .

# Build
RUN dotnet build "rinhaBackend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "rinhaBackend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "rinhaBackend.dll"]