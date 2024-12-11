FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
#EXPOSE 8080
#EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "BellHopBot.csproj"
WORKDIR "/src"
RUN dotnet build "BellHopBot.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BellHopBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN mkdir /app/db
RUN mkdir /app/logs

USER root
RUN chown root:root db && chmod a+w db
USER $APP_UID

EXPOSE 80
EXPOSE 443

ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "BellHopBot.dll"]
