[![Build Status](https://dev.azure.com/accountgo/accountgo/_apis/build/status/AccountGo-Nightly-Build)](https://dev.azure.com/accountgo/accountgo/_build/latest?definitionId=10)

# AccountGo
Accounting System built on DOTNETCORE, an opensource and cross platform (ASP.NET + ReactJS on the Frontend). It's in early stage and still lots of work to do but happy to share it to anyone. This will be very useful if you have future project to develop accounting system. We do the hard work for you!
It is initially designed for a small size businesses and the idea is to help them running efficient business by using Accounting System fit to them.

### IMPORTANT NOTE:
You can use MacOS, Linux, Windows to develop and deploy this project. Make sure you have the latest .net core 2.1 sdk and runtime installed. Go to https://www.microsoft.com/net/download/dotnet-core/2.1 to download the installer. We are also experimenting F# + microservice on some parts.

# Features
On a high level, this solution will provide modules including but not limited to

1. Accounts Receivable
2. Accounts Payable
3. Inventory Control
4. Financial/Accounting

# Setup Development Environment (Visual Studio Code)
AccountGoWeb and Api projects are using ASP.NET Core 2.1.

-   “AccountGoWeb” requires webpack, webpack-cli, gulp, typescript installed
-   “Api” – ASP.NET REST API project to be consumed by “AccountGoWeb”

1. Install Visual Studio Code.
1. Clone/Fork the latest repo here https://github.com/AccountGo/accountgo
1. Build each projects in this order. Core->Services->Dto->Api->AccountGoWeb. To build the project, change directory to project folder, execute "dotnet restore" then "dotnet build". Make sure all projects build successfully. Alternatively, use accountgo.sln file to use by dotnet build. To do this change directory to "src" folder, execute "dotnet restore", then "dotnet build".

Note: Preceding steps confirms all projects can build successfully using "dotnet build". Succeeding steps will provide specific instructions to build and run Api (Back-end), asnd AccountGoWeb (Front-end), as well as how to setup database. Let's start on database setup first.

# Setup SQL Server (Using Docker is optional)
You can opt to install SQL Server or use docker image (like we do). Assuming you have docker installed (make sure to use linux container), follow the steps below. (Install docker if you haven't done so.)
1. Open command prompt (terminal for MacOS).
1. Execute "docker pull microsoft/mssql-server-linux". We prefer to use SQL Server for Linux for lightweight.
1. Run sql server for linux. Execute "docker run -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=Str0ngPassword' -p 1433:1433 -d --name=local-mssql microsoft/mssql-server-linux". Note: If you are encountering issue where the docker container close immediately, try to use "docker-compose up" but be sure to comment out "web" and "api" services so that "db" service the only service that will be configure to run.

# Check SQL Server connection
1. Download SQL Operation Studio by Microsoft to manage the SQL Server. https://docs.microsoft.com/en-us/sql/sql-operations-studio/download?view=sql-server-2017
1. Open SQL Operation Studio and connect to your running SQL Server docker container.

# Publish Database (Create Tables, Foreign Keys, and some Initial Data)
The "database/scripts" folder contains the sql scripts to execute. For the purpose of db migration, you can use DbUp [https://dbup.readthedocs.io] tool and create your own migration console. For simplicity and a quick one, download this cool tool we used in this guide. https://richardtasker.co.uk/2018/09/15/introducing-dotnet-db-migrate/#.W6PIJJMza3U - checkout this site and download. Or directly execute this .net cli "dotnet tool install --global dotnet-db-migrate --version 1.0.0" in the command prompt. Confirm if installed properly, execute "dotnet-db-migrate -h".

Once "dotnet-db-migrate" already installed, execute the following in this specfic order:

1. In root folder "accountgo", open command prompt or terminal (macos) and run the following.
1. dotnet-db-migrate "Data Source=localhost;User ID=sa;Password=Str0ngPassword;Initial Catalog=accountgo;" -s ./db/scripts/tables  --ensure-db-exists
1. dotnet-db-migrate "Data Source=localhost;User ID=sa;Password=Str0ngPassword;Initial Catalog=accountgo;" -s ./db/scripts/foreign_keys  --ensure-db-exists
1. dotnet-db-migrate "Data Source=localhost;User ID=sa;Password=Str0ngPassword;Initial Catalog=accountgo;" -s ./db/scripts/initial_data  --ensure-db-exists

### Note: The initial data in the previous steps only include the security initial data. There's more data initialization to in the preceding instructions

## Build and run "Api" (Back-end)
1. Change directory to Api project folder
1. Build the project "dotnet build"
1. Update database connection. Open "appsettings.json" and change the connection string. You may want to change "DevelopmentConnectionString" as we will be running the api in "Development" mode shortly.
1. Run the api, execute "dotnet run". Note that there is no launchsettings.json included in the repository, thus, the following bullets are important.
    * To run in development mode, execute "dotnet run --environment Development"
    * To change it to specific port, execute "dotnet run --environment Development server.urls=http://+:8001". It could be any port as you like, but the front-end is hard-coded to call api on port http://localhost:8001. So change the front-end as too. By default, port is open to 5000 and 5001 (http and https respectively).
1. To test if Api is running correctly, you can simply call one "GET" endpoint. e.g. http://localhost:8001/api/sales/customers. This will return list of customers in JSON format.

# Build and run "AccountGoWeb" (Front-end)
1. AccountGoWeb require more steps to completely build the front-end artifacts. To do this, follow the succeeding steps
1. Change directory to src/AccountGoWeb and open Visual Studio Code terminal
1. Install all npm packages by executing "npm install"
1. Install typescript by executing "npm install -g typescript"
1. Install webpack-cli by executing "npm install -g webpack-cli"
1. Install webpack by executing "npm install -g webpack"
1. Install gulp by executing install -g gulp"
1. Still in the same folder, type and enter the following in command prompt
1. "gulp" (This will run the gulpfile.js)
1. "tsc" (This will run the tsconfig.json)
1. "webpack" (This will run the webpack.config.js)
1. And lastly, in src/AccountGoWeb terminal, execute "dotnet build"
1. Run the "AccoungGoWeb" project, execute "dotnet run". Note that there is no launchsettings.json included in the repository, thus, the following bullets are important.
    * To run in development mode, execute "dotnet run --environment Development"
    * To change it to specific port, execute "dotnet run --environment Development server.urls=http://+:8000". It could be any port as you like. By default, port is open to 5000 and 5001 (http and https respectively).
1. To test if AccountGoWeb UI is running correctly, open your browser to http://localhost:8000

### UPDATE: Above steps are still valid, however, "dotnet run --environment Development server.urls=http://+:8000" will automatically execute gulp, tsc, and webpack commands.

### IMPORTANT  NOTE:
Your wwwroot folder should be look like this if you correctly followed the steps above.


![AccountGo](https://user-images.githubusercontent.com/17961526/46572613-ab037d00-c9bb-11e8-9e59-6ed84fc1a04d.png)

# Initialize Data
At this point, your database has no data on it. But there is already an initial username and password (admin@accountgo.ph/P@ssword1) and you can logon to the UI. Now lets, create some initial data that would populate the following models.
- Company
- Chart of accounts/account classes
- Financial year
- Payment terms
- GL setting
- Tax
- Vendor
- Customer
- Items
- Banks

To initialialized a company, call the api endpoint directly http://localhost:8001/api/administration/initializedcompany from the browser or by using curl e.g. curl http://localhost:8001/api/administration/initializedcompany. If you encounter some issues, the easy way for now is recreate your database and repeat the "Publish Database" section.

# Using docker-compose
To run everything (database, api, web) in docker container you can use docker-compose.yml
1. Make sure to change directory to the root folder "accountgo"
1. Execute "docker-compose up" or "docker-compose up --build"
1. Create database, tables, foreign keys, and some initial data. To do this go to "Publish Database (Create Tables, Foreign Keys, and some Initial Data)" above.
1. Initialize data by calling a special api endpoint directly from the browser or curl. e.g. curl http://localhost:8001/api/administration/initializedcompany

### SUMMARY: At this point, you should have:
1. Database instance running in docker container and you can connect to it
1. You should have a running "Api" and can test it by getting the list of customers e.g. http://localhost:8001/api/sales customers
1. You can browse the UI from http://localhost:8000 and able to login to the system using initial username/password: admin@accountgo.ph/P@ssword1
1. Initialize data by calling a special api endpoint directly. http://localhost:8001/api/administration/initializedcompany

## Technology Stack:
- ASP.NET Core 2.1
- ReactJS
- MobX, React-MobX
- Axios
- Bootstrap
- D3
- React-router (on some pages)
- Typescript

Demo site (new UI) : http://accountgo.net

Dark theme

![accountgoweb](https://user-images.githubusercontent.com/17961526/46572651-501e5580-c9bc-11e8-8f3a-336555add757.png)
![accountgoweb](https://user-images.githubusercontent.com/17961526/46572643-30872d00-c9bc-11e8-912d-8307fdd94027.png)
             

# Help Wanted
If you are a developer and wanted to take part as contributor/collaborator we are happy to welcome you! To start with, you can visit the issues page and pick an issue that you would like to work on.

So go ahead, add your code and make your first pull request.

# Contact Support
Feel free to email mvpsolution@gmail.com of any questions.