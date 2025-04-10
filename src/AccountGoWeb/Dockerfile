FROM mcr.microsoft.com/dotnet/sdk:8.0-bullseye-slim-amd64 AS build-env

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

WORKDIR /app/AccountGoWeb

RUN npm install --legacy-peer-deps
RUN npm rebuild node-sass
RUN npm run css

WORKDIR /app

### Add all modules here. TODO: Improve in such a way all projects under Modules folder are automatically build.
RUN dotnet restore ./Modules/SampleModule/*.csproj
RUN dotnet build ./Modules/SampleModule/*.csproj

RUN dotnet restore ./AccountGoWeb/*.csproj
RUN dotnet publish ./AccountGoWeb/*.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bullseye-slim-amd64

WORKDIR /app

# This APIHOST environment variable is not a duplicate of above declaration
ARG APIHOST api
ENV APIHOST ${APIHOST}

ARG ASPNETCORE_ENVIRONMENT Development
ENV ASPNETCORE_ENVIRONMENT ${ASPNETCORE_ENVIRONMENT}

COPY --from=build-env /app/out ./

EXPOSE 8000

CMD ["dotnet", "AccountGoWeb.dll"]