# Central .NET 10 thin manifest for CTMS microservice
ARG APP_NAME=ctms
ARG PROJECT_PATH=apps/ctms/src

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-bookworm-slim AS dev
WORKDIR /workspace
COPY Cadence.sln Directory.Build.props Directory.Packages.props .editorconfig global.json ./
COPY . .
CMD ["sh", "-c", "dotnet watch --project apps/ctms/src run"]

FROM dev AS build
RUN dotnet restore apps/ctms/src && dotnet publish apps/ctms/src -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-bookworm-slim AS runtime
WORKDIR /app
USER app
COPY --from=build /app/publish .
EXPOSE 8000
ENTRYPOINT ["sh", "-c", "exec dotnet Cadence.Apps.Ctms.dll"]
