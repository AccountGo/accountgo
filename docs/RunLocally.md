## Build and run api
1. Change directory to Api project folder
1. Build the project "dotnet build"
1. Update database connection. Open "appsettings.json" and change the connection string. You may want to change "DevelopmentConnectionString" as we will be running the api in "Development" mode shortly.
1. Run the api, execute "dotnet run". Note that there is no launchsettings.json included in the repository, thus, the following bullets are important.
    * To run in development mode, execute "dotnet run --environment Development"
    * To change it to specific port, execute "dotnet run --environment Development server.urls=http://+:8001". It could be any port as you like, but the front-end is hard-coded to call api on port http://localhost:8001. So change the front-end as too. By default, port is open to 5000 and 5001 (http and https respectively).
1. To test if Api is running correctly, you can simply call one "GET" endpoint. e.g. http://localhost:8001/api/sales/customers. This will return list of customers in JSON format.

## Build and run front-end
1. Change directory to AccountGoWeb
