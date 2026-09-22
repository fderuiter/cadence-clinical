# Central .NET 10 thin manifest for Tickets microservice
ARG APP_NAME=tickets
ARG PROJECT_PATH=apps/tickets/src

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-bookworm-slim AS dev
WORKDIR /workspace
COPY Cadence.sln Directory.Build.props Directory.Packages.props .editorconfig global.json ./
COPY . .
CMD ["sh", "-c", "dotnet watch --project apps/tickets/src run"]

FROM dev AS build
RUN dotnet restore apps/tickets/src && dotnet publish apps/tickets/src -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-bookworm-slim AS runtime
WORKDIR /app
USER app
COPY --from=build /app/publish .
EXPOSE 8000
ENTRYPOINT ["sh", "-c", "exec dotnet Cadence.Apps.Tickets.dll"]
