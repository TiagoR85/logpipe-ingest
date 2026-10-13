FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ADD https://github.com/TiagoR85/logpipe-contracts.git#v1.0.0 /tmp/contracts
RUN dotnet pack /tmp/contracts/src/LogPipe.Contracts/LogPipe.Contracts.csproj -c Release -o /tmp/contract-packages
COPY . .
RUN mkdir -p /src/packages && cp /tmp/contract-packages/*.nupkg /src/packages/
RUN dotnet publish src/LogPipe.Ingest/LogPipe.Ingest.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8090
EXPOSE 8090
USER $APP_UID
ENTRYPOINT ["dotnet", "LogPipe.Ingest.dll"]
