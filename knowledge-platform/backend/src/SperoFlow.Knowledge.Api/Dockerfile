FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/SperoFlow.Knowledge.Contracts/SperoFlow.Knowledge.Contracts.csproj backend/src/SperoFlow.Knowledge.Contracts/
COPY backend/src/SperoFlow.Knowledge.Domain/SperoFlow.Knowledge.Domain.csproj backend/src/SperoFlow.Knowledge.Domain/
COPY backend/src/SperoFlow.Knowledge.Infrastructure/SperoFlow.Knowledge.Infrastructure.csproj backend/src/SperoFlow.Knowledge.Infrastructure/
COPY backend/src/SperoFlow.Knowledge.Api/SperoFlow.Knowledge.Api.csproj backend/src/SperoFlow.Knowledge.Api/
RUN dotnet restore backend/src/SperoFlow.Knowledge.Api/SperoFlow.Knowledge.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/src/SperoFlow.Knowledge.Api/SperoFlow.Knowledge.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 app \
    && useradd --uid 10001 --gid app --create-home --shell /usr/sbin/nologin app \
    && mkdir -p /var/lib/speroflow/keys \
    && chown -R app:app /var/lib/speroflow
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
COPY backend/docker/entrypoint-knowledge-dotnet.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh
USER app
EXPOSE 8080
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "SperoFlow.Knowledge.Api.dll"]
