# Build context: repository root
# docker compose build with context: . and dockerfile: src/Assistant.Mcp.Coolify/Dockerfile

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json .editorconfig ./
COPY src/Assistant.Mcp.Coolify/ ./src/Assistant.Mcp.Coolify/
RUN dotnet restore src/Assistant.Mcp.Coolify/Assistant.Mcp.Coolify.csproj
RUN dotnet publish src/Assistant.Mcp.Coolify/Assistant.Mcp.Coolify.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Assistant.Mcp.Coolify.dll"]
