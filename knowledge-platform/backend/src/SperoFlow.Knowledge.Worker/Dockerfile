FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/SperoFlow.Knowledge.Contracts/SperoFlow.Knowledge.Contracts.csproj backend/src/SperoFlow.Knowledge.Contracts/
COPY backend/src/SperoFlow.Knowledge.Domain/SperoFlow.Knowledge.Domain.csproj backend/src/SperoFlow.Knowledge.Domain/
COPY backend/src/SperoFlow.Knowledge.Infrastructure/SperoFlow.Knowledge.Infrastructure.csproj backend/src/SperoFlow.Knowledge.Infrastructure/
COPY backend/src/SperoFlow.Knowledge.Worker/SperoFlow.Knowledge.Worker.csproj backend/src/SperoFlow.Knowledge.Worker/
RUN dotnet restore backend/src/SperoFlow.Knowledge.Worker/SperoFlow.Knowledge.Worker.csproj

COPY backend/ backend/
RUN dotnet publish backend/src/SperoFlow.Knowledge.Worker/SperoFlow.Knowledge.Worker.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN groupadd --gid 10001 app \
    && useradd --uid 10001 --gid app --create-home --shell /usr/sbin/nologin app
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
COPY backend/docker/entrypoint-knowledge-dotnet.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh
USER app
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "SperoFlow.Knowledge.Worker.dll"]
