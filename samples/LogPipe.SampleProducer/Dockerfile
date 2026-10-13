FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN mkdir -p /src/packages
RUN dotnet publish samples/LogPipe.SampleProducer/LogPipe.SampleProducer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "LogPipe.SampleProducer.dll"]
