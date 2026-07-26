FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/SperoFlow.Domain/SperoFlow.Domain.csproj backend/src/SperoFlow.Domain/
COPY backend/src/SperoFlow.Contracts/SperoFlow.Contracts.csproj backend/src/SperoFlow.Contracts/
COPY backend/src/SperoFlow.Application/SperoFlow.Application.csproj backend/src/SperoFlow.Application/
COPY backend/src/SperoFlow.Infrastructure/SperoFlow.Infrastructure.csproj backend/src/SperoFlow.Infrastructure/
COPY backend/src/SperoFlow.Migrator/SperoFlow.Migrator.csproj backend/src/SperoFlow.Migrator/
RUN dotnet restore backend/src/SperoFlow.Migrator/SperoFlow.Migrator.csproj

COPY backend/ backend/
RUN dotnet publish backend/src/SperoFlow.Migrator/SperoFlow.Migrator.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN groupadd --gid 10001 app \
    && useradd --uid 10001 --gid app --create-home --shell /usr/sbin/nologin app
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
COPY infrastructure/docker/entrypoint-dotnet.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh
USER app
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "SperoFlow.Migrator.dll"]
