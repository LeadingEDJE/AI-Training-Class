# ==============================================================================
# .NET API Dockerfile -- Multi-stage (dev / build / prod)
# ==============================================================================

# --- BUILD stage (CI / release builds) ---------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /app

# Copy only what's needed for publish (not test projects)
COPY Directory.Build.props ./
COPY api/ api/
RUN dotnet publish api -c Release -o /out

# --- PROD stage (minimal runtime image) --------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS prod

WORKDIR /app
COPY --from=build /out .

EXPOSE 5000

ENTRYPOINT ["dotnet", "LeadingEDJE.Leap.Api.dll"]

# --- DEV stage (used by Docker Compose for local development) -----------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev

WORKDIR /app

# Enable polling file watcher for dotnet watch inside containers
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=true

# Copy project files first for restore layer caching
COPY api/LeadingEDJE.Leap.Api.csproj api/
COPY tests/unit/LeadingEDJE.Leap.Api.Tests.csproj tests/unit/
COPY tests/integration/LeadingEDJE.Leap.Api.IntegrationTests.csproj tests/integration/
COPY leap.slnx ./
COPY Directory.Build.props ./
COPY nuget.config ./

# Mount host NuGet credentials for LeadingEDJE GitHub Packages feed auth.
# Secret is mounted from ~/.nuget/NuGet/NuGet.Config via compose.yaml build secrets.
# It's only available during this RUN step and never baked into the image.
RUN --mount=type=secret,id=nuget_config,target=/root/.nuget/NuGet/NuGet.Config \
    dotnet restore

# Copy full source
COPY . .

ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile", "--project", "api"]
