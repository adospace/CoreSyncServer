FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV NODE_VERSION=22
RUN curl -fsSL https://deb.nodesource.com/setup_${NODE_VERSION}.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Copy solution and project files
COPY src/CoreSyncServer.slnx src/
COPY src/Server/CoreSyncServer.csproj src/Server/
COPY src/Server/package.json src/Server/
COPY src/CoreSyncServer.Data/CoreSyncServer.Data.csproj src/CoreSyncServer.Data/
COPY src/Services/CoreSyncServer.Services.csproj src/Services/
COPY src/WebClient/CoreSyncServer.Client.csproj src/WebClient/

# Copy CoreSync submodule projects
COPY CoreSync/src/CoreSync/CoreSync.csproj CoreSync/src/CoreSync/
COPY CoreSync/src/CoreSync.PostgreSQL/CoreSync.PostgreSQL.csproj CoreSync/src/CoreSync.PostgreSQL/
COPY CoreSync/src/CoreSync.Sqlite/CoreSync.Sqlite.csproj CoreSync/src/CoreSync.Sqlite/
COPY CoreSync/src/CoreSync.SqlServer/CoreSync.SqlServer.csproj CoreSync/src/CoreSync.SqlServer/
COPY CoreSync/src/CoreSync.SqlServerCT/CoreSync.SqlServerCT.csproj CoreSync/src/CoreSync.SqlServerCT/

# Install npm dependencies for Linux (ignore any Windows lock file)
WORKDIR /src/src/Server
RUN rm -f package-lock.json && npm install
WORKDIR /src

# Restore
RUN dotnet restore src/Server/CoreSyncServer.csproj

# Copy everything and build
COPY CoreSync/ CoreSync/
COPY src/ src/

WORKDIR /src/src/Server
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CoreSyncServer.dll"]
