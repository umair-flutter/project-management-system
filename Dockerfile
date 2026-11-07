# Build: dotnet SDK image publishes the app; runtime: slim ASP.NET image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "Project Managemnet System/Project Managemnet System.csproj" -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/data && chown -R $APP_UID /app
USER $APP_UID

# Port 8080, trust the hosting proxy's HTTPS headers, keep memory low for small instances.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0 \
    Database__Provider=Sqlite \
    ConnectionStrings__Sqlite="Data Source=/app/data/app.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "Project Managemnet System.dll"]
