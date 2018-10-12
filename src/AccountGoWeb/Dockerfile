FROM microsoft/dotnet:2.1.301-sdk AS build-env
WORKDIR /app

COPY ./src/Dto Dto
COPY ./src/AccountGoWeb AccountGoWeb

ENV APIHOST api
ENV NODE_VERSION 10.11.0
ENV NODE_DOWNLOAD_SHA 4d8aaf8c1c51acbbb46bbd4e3c924a573884603b1c4e35cc02982bbda9779c8b
RUN curl -SL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.gz" --output nodejs.tar.gz \
    && echo "$NODE_DOWNLOAD_SHA nodejs.tar.gz" | sha256sum -c - \
    && tar -xzf "nodejs.tar.gz" -C /usr/local --strip-components=1 \
    && rm nodejs.tar.gz \
    && ln -s /usr/local/bin/node /usr/local/bin/nodejs

WORKDIR /app/AccountGoWeb

RUN npm install
RUN npm run css

RUN dotnet publish AccountGoWeb.csproj -c Release -o out

FROM microsoft/dotnet:2.1.1-aspnetcore-runtime-alpine3.7
# This APIHOST environment variable is not a duplicate of above declaration
ENV APIHOST api
WORKDIR /app
EXPOSE 8000

COPY --from=build-env /app/AccountGoWeb/out ./

ENTRYPOINT ["dotnet", "AccountGoWeb.dll"]