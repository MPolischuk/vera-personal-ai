# Build context: repository root
# docker compose build with context: . and dockerfile: src/Assistant.Api/Dockerfile

FROM node:22-alpine AS web
WORKDIR /web
COPY src/clients/web/package.json src/clients/web/package-lock.json ./
RUN npm ci
COPY src/clients/web/ ./
# Same-origin in the container: empty base URL talks to the API host.
# An empty bearer arg is the password-mode build. PC Compose still injects it.
ARG VITE_BEARER_TOKEN=
ENV VITE_API_BASE_URL=
ENV VITE_BEARER_TOKEN=$VITE_BEARER_TOKEN
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json personal_ai_assistant.slnx .editorconfig ./
COPY compose.yaml compose.production.yaml ./
COPY src/ ./src/
RUN dotnet restore src/Assistant.Api/Assistant.Api.csproj
RUN dotnet publish src/Assistant.Api/Assistant.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
COPY --from=web /web/dist ./wwwroot
# Non-secret Production defaults (Models, Costs, etc.). Secrets come from compose env.
COPY src/Assistant.Api/appsettings.Example.json ./appsettings.Production.json
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Assistant.Api.dll"]
