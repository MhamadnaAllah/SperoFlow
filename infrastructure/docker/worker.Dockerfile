FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/SperoFlow.Domain/SperoFlow.Domain.csproj backend/src/SperoFlow.Domain/
COPY backend/src/SperoFlow.Contracts/SperoFlow.Contracts.csproj backend/src/SperoFlow.Contracts/
COPY backend/src/SperoFlow.Application/SperoFlow.Application.csproj backend/src/SperoFlow.Application/
COPY backend/src/SperoFlow.Infrastructure/SperoFlow.Infrastructure.csproj backend/src/SperoFlow.Infrastructure/
COPY backend/src/SperoFlow.Worker/SperoFlow.Worker.csproj backend/src/SperoFlow.Worker/
COPY backend/src/SperoFlow.Infrastructure/Directory.Build.props backend/src/SperoFlow.Infrastructure/
RUN dotnet restore backend/src/SperoFlow.Worker/SperoFlow.Worker.csproj

COPY backend/ backend/
RUN dotnet publish backend/src/SperoFlow.Worker/SperoFlow.Worker.csproj --configuration Release --output /app/publish --no-restore

# Needs aspnet (not plain runtime): Infrastructure adds a FrameworkReference on Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The .NET 10 base image already provides a non-root 'app' user/group.
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
COPY infrastructure/docker/entrypoint-dotnet.sh /entrypoint.sh
RUN chmod 0555 /entrypoint.sh
USER app
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "SperoFlow.Worker.dll"]
