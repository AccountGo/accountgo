ARG BUILDPLATFORM="linux/amd64"
FROM --platform=${BUILDPLATFORM} mcr.microsoft.com/dotnet/sdk:7.0 AS build-env

WORKDIR /app

COPY ./src/Api Api
COPY ./src/Core Core
COPY ./src/Services Services
COPY ./src/Dto Dto

RUN ls -a

RUN dotnet restore ./Api/*.csproj
RUN dotnet publish ./Api/*.csproj -c Release -o out

FROM --platform=${BUILDPLATFORM} mcr.microsoft.com/dotnet/runtime:latest

ARG DBSERVER localhost
ARG DBUSERID dbuser
ARG DBPASSWORD Str0ngPassword
ARG DBNAME accountgodb

ENV DBSERVER ${DBSERVER}
ENV DBUSERID ${DBUSERID}
ENV DBPASSWORD ${DBPASSWORD}
ENV DBNAME ${DBNAME}

WORKDIR /app

COPY --from=build-env /app/out ./

EXPOSE 8001
CMD ["dotnet", "Api.dll"]