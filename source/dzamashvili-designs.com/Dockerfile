FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY App.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Data/*.json and the project images are edited live through /admin and left out of publish:
# mount volumes here so they survive container rebuilds.
RUN mkdir -p Data wwwroot/projects/images && chown -R $APP_UID:$APP_UID Data wwwroot/projects/images
VOLUME ["/app/Data", "/app/wwwroot/projects/images"]

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "App.dll"]
