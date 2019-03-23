FROM mcr.microsoft.com/dotnet/core/sdk AS build-env
WORKDIR /app

COPY ./src/Dto Dto
COPY ./src/Infrastructure Infrastructure
COPY ./src/AccountGoWeb AccountGoWeb
COPY ./src/Modules Modules

ENV APIHOST api
ENV NODE_VERSION 10.15.1
ENV NODE_DOWNLOAD_SHA ca1dfa9790876409c8d9ecab7b4cdb93e3276cedfc64d56ef1a4ff1778a40214
RUN curl -SL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.gz" --output nodejs.tar.gz \
    && echo "$NODE_DOWNLOAD_SHA nodejs.tar.gz" | sha256sum -c - \
    && tar -xzf "nodejs.tar.gz" -C /usr/local --strip-components=1 \
    && rm nodejs.tar.gz \
    && ln -s /usr/local/bin/node /usr/local/bin/nodejs

### Add all modules here
WORKDIR /app/Modules
RUN dotnet publish SampleModule/SampleModule.csproj -c Release -o out

WORKDIR /app/AccountGoWeb

RUN npm install
RUN npm rebuild node-sass
RUN npm run css

RUN dotnet publish AccountGoWeb.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/core/runtime:2.2-alpine3.9
# This APIHOST environment variable is not a duplicate of above declaration
ENV APIHOST api
WORKDIR /app
EXPOSE 8000

COPY --from=build-env /app/AccountGoWeb/out ./

ENTRYPOINT ["dotnet", "AccountGoWeb.dll"]