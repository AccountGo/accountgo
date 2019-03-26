FROM mcr.microsoft.com/dotnet/core/sdk AS build-env
WORKDIR /app

COPY ./src/Dto Dto
COPY ./src/Infrastructure Infrastructure
COPY ./src/AccountGoWeb AccountGoWeb
COPY ./src/Modules Modules
COPY ./Directory.Build.props .
COPY ./Directory.Build.targets .

# APIURLSPA and NODE_ENV variables are for webpack used. http://accountgo.net/spaproxy?endpoint=, http://localhost:8000/api
ENV APIURLSPA http://container-test.azurewebsites.net/spaproxy?endpoint=
ENV NODE_ENV Production
ENV NODE_VERSION 10.15.1
ENV NODE_DOWNLOAD_SHA ca1dfa9790876409c8d9ecab7b4cdb93e3276cedfc64d56ef1a4ff1778a40214

RUN curl -SL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.gz" --output nodejs.tar.gz \
    && echo "$NODE_DOWNLOAD_SHA nodejs.tar.gz" | sha256sum -c - \
    && tar -xzf "nodejs.tar.gz" -C /usr/local --strip-components=1 \
    && rm nodejs.tar.gz \
    && ln -s /usr/local/bin/node /usr/local/bin/nodejs

### Add all modules here. TODO: Improve in such a way all projects under Modules folder are automatically build.
RUN dotnet build Modules/SampleModule/SampleModule.csproj -c Release

WORKDIR /app/AccountGoWeb

RUN npm install
RUN npm rebuild node-sass
RUN npm run css

RUN dotnet publish -c Release

FROM mcr.microsoft.com/dotnet/core/runtime:2.2-alpine3.9
# This APIHOST environment variable is not a duplicate of above declaration
ENV APIHOST api
WORKDIR /app
EXPOSE 8000

COPY --from=build-env /app/.build/bin/AccountGoWeb/Release/netcoreapp2.2/publish ./

ENTRYPOINT ["dotnet", "AccountGoWeb.dll"]