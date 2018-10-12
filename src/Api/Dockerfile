FROM microsoft/dotnet:2.1.301-sdk AS build-env
WORKDIR /app

COPY ./src/Core Core
COPY ./src/Dto Dto
COPY ./src/Services Services
COPY ./src/Api Api

RUN dotnet publish ./Api/*.csproj -c Release -o out

FROM microsoft/dotnet:2.1.1-aspnetcore-runtime-alpine3.7
WORKDIR /app
EXPOSE 8001

COPY --from=build-env /app/Api/out ./

ENTRYPOINT ["dotnet", "Api.dll"]