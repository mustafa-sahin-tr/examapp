# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY . .

ARG PROJECT_PATH
RUN test -n "$PROJECT_PATH"

RUN dotnet restore "$PROJECT_PATH" --ignore-failed-sources --source https://api.nuget.org/v3/index.json
RUN dotnet publish "$PROJECT_PATH" -c Release -o /app/publish /p:UseAppHost=false

# aspnet, not runtime: OutboxPublisherService references Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

COPY --from=build /app/publish/ ./

ARG APP_DLL
RUN test -n "$APP_DLL"

ENV APP_DLL=${APP_DLL}

ENTRYPOINT ["sh", "-c", "dotnet \"$APP_DLL\"" ]
