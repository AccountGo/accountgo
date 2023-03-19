ARG BUILDPLATFORM="linux/amd64"
FROM --platform=${BUILDPLATFORM} mcr.microsoft.com/dotnet/sdk AS build-env

WORKDIR /app

COPY ./src/Dto Dto
COPY ./src/Infrastructure Infrastructure
COPY ./src/AccountGoWeb AccountGoWeb
COPY ./src/Modules Modules
COPY ./Directory.Build.props .
COPY ./Directory.Build.targets .

# APIURLSPA and NODE_ENV variables are for webpack used. http://accountgo.net/spaproxy?endpoint=, http://localhost:8000/api
ENV APIURLSPA http://localhost:8000/spaproxy?endpoint=
ENV NODE_ENV Production
ENV NODE_VERSION 19.8.1
ENV NODE_DOWNLOAD_SHA 77b47ce0ed17ef90c1df6ef1ca6ea0f1375346ae4d5099708d9ee39d805f9d6e

RUN curl -SL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.gz" --output nodejs.tar.gz \
    && echo "$NODE_DOWNLOAD_SHA nodejs.tar.gz" | sha256sum -c - \
    && tar -xzf "nodejs.tar.gz" -C /usr/local --strip-components=1 \
    && rm nodejs.tar.gz \
    && ln -s /usr/local/bin/node /usr/local/bin/nodejs

### Add all modules here. TODO: Improve in such a way all projects under Modules folder are automatically build.
WORKDIR /app/Modules/SampleModule

RUN dotnet restore
RUN dotnet build

WORKDIR /app/AccountGoWeb

RUN npm install
RUN npm run gulp
RUN npm run tsc
RUN npm run webpack
RUN npm rebuild node-sass
RUN npm run css

RUN dotnet publish -c Release

FROM --platform=${BUILDPLATFORM} mcr.microsoft.com/dotnet/runtime:latest

# This APIHOST environment variable is not a duplicate of above declaration
ENV APIHOST api

ARG ASPNETCORE_ENVIRONMENT Development
ENV ASPNETCORE_ENVIRONMENT ${ASPNETCORE_ENVIRONMENT}

WORKDIR /app

EXPOSE 8000

COPY --from=build-env /app/.build/bin/AccountGoWeb/Release/netcoreapp2.2/publish ./

ENTRYPOINT ["dotnet", "AccountGoWeb.dll"]