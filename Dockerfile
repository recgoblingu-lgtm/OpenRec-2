FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore OpenRec-2.csproj
RUN dotnet publish OpenRec-2.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
RUN adduser --disabled-password --gecos "" --uid 10001 appuser
COPY --from=build /app/publish .
RUN mkdir -p /app/Storage && chown -R appuser:appuser /app
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpenRec-2.dll"]
