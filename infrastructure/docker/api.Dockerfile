FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/SperoFlow.Domain/SperoFlow.Domain.csproj backend/src/SperoFlow.Domain/
COPY backend/src/SperoFlow.Contracts/SperoFlow.Contracts.csproj backend/src/SperoFlow.Contracts/
COPY backend/src/SperoFlow.Application/SperoFlow.Application.csproj backend/src/SperoFlow.Application/
COPY backend/src/SperoFlow.Infrastructure/SperoFlow.Infrastructure.csproj backend/src/SperoFlow.Infrastructure/
COPY backend/src/SperoFlow.Api/SperoFlow.Api.csproj backend/src/SperoFlow.Api/
RUN dotnet restore backend/src/SperoFlow.Api/SperoFlow.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/src/SperoFlow.Api/SperoFlow.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The .NET 10 aspnet base image already provides a non-root 'app' user/group,
# so we only install curl here (no duplicate groupadd/useradd, which would fail
# with "group 'app' already exists").
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
COPY infrastructure/docker/entrypoint-dotnet.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh
USER app
EXPOSE 8080
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "SperoFlow.Api.dll"]
