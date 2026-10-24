FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/ .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Seed CSVs (skills, roles, quiz questions) are read from /app/data
COPY data/ ./data/
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "VezaSkills.Api.dll"]
