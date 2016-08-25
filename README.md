# AccountGo
Accounting System built in ASP.NET MVC is in early stage and lots of work to do but happy to share it to anyone. This will be very useful if you have future project to develop accounting system. We do the hard work for you!
It is initially designed for a small size businesses and the idea is to help them running efficient business by using Accounting System fit to them.

# Features
On a high level, this solution will provide modules including

1. Accounts Receivable
2. Accounts Payable
3. Inventory Control
4. Financial/Accounting

#FRONT-END
The screenshot below will be the future front-end. It is heavily under-development and you could be part of it. The project is "AccountGoWeb" and consuming the "Api" project.

Technology Stack:
- ASP.NET Core
- ReactJS
- MobX, React-MobX
- Axios
- Bootstrap
- D3
- React-router (on some pages)

Demo site (new UI) : http://www.accountgo.ph/

![accountgoweb](https://cloud.githubusercontent.com/assets/17961526/17953180/d2e7aac2-6aa3-11e6-8150-fe1b8274cf91.png)
![accountgoweb](https://cloud.githubusercontent.com/assets/17961526/17430653/0cf89cca-5b28-11e6-81dd-5f14695c8cfc.png)

# Setup Develoment Environment
In “Presentation” folder, there are three web projects. 
-	“Web” – an old web front-end. Currently the demo site is http://www.accountgo.ph
-	“AccountGoWeb” – this is the future. Functionalities from the old "Web" front-end are soon transported to this project. Development under ASP.NET Core 1.0, ReactJS and MobX.
-	“Api” – ASP.NET REST API project to be consumed by “AccountGoWeb”.

AccountGoWeb/Api projects using ASP.NET Core 1.0, and requires you to install VS 2015 (Community is Ok) with Update 3 and the latest update of “Microsoft ASP.NET and Web Tools” extension.

“AccountGoWeb” use webpack so you also need to install webpack to the project. For some reason, Visual Studio 2015 "dependencies" is showing warning "Dependencies - not installed" when adding webpack to project.json. So for the time being, you need to install webpack manually. See instructions below.

1. Install Visual 2015 (i.e. Community edition) with Update 3.
2. Update “Microsoft ASP.NET and Web Tools” external tools. After you install VS 2015, go to "Tools->Extensions and updates" and search for “Microsoft ASP.NET and Web Tools”.
3. Clone/Fork the latest repo here https://github.com/AccountGo/accountgo
4. Install “Webpack”. open command prompt and go to “AccountGoWeb” folder. Type “npm install webpack”.
5. Install "Webpack Task Runner" Visual Studio 2015 extension. Go to "Tools->Extensions and updates" and search for “Webpack Task Runner”.
6. Open the solution in VS and restore all packages.
7. Run webpack and gulpfile in Task Runner.
8. Set "accountgo" solution to "Multiple Startup Projects". Select "AccountGoWeb" and "Api".
9. Open "AccountGoWeb/webpack.config.js", set 'Config' apiUrl: "http://localhost:5000/"

# Publish Database
1. Open solution in VS. The SQL Database project is under Database\SQL.Accountgo
2. Right click the project Database\SQL.Accountgo and select Rebuild.
3. Right click the project Database\SQL.Accountgo and select Publish.
4. In "Target database connection", click "Edit" button.
5. Select existing database connection or create a new one.

# Run "Api" project
1. Right click on the project properties and go to Debug. Select "AccountGoApi" from the profile. This by default will run the api on "http://localhost:5000"
2. In api/appsettings.json, update properly your "LocalConnection" connection string.
3. In api/Startup.cs, then ConfigureServices method, set connectionString = Configuration["Data:LocalConnection:ConnectionString"];

# Initialized Data
On your first run, if you successfully Publish Database\SQL.AccountGo you will get an empty database. Let's get your DB Initialized with sample master data.'
1. Create your first user. registration page is hidden but you can go directly from your browser. (e.g. http://localhost/account/register)
2. Set password like 'P@ssword1'. More than 6 chars, 1 Caps, 1 special, 1 number. Note: Currently, even successfully register a new user, it will not inform you. Just try to login using your newly created user. This registratio page is temporary.
3. After successful user registration, the system automatically runs data initialization of the following.
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
             

# Help Wanted
Wether you are a Developer, Consultant, Accountant, QA, Marketing expert, Project Manager we can all be part of this great and promising project.

If you are a developer and wanted to take part as contributor/collaborator we are happy to welcome you! To start with, you can visit the issues page and pick an issue that you would like to work on.

So go ahead, add your code. Looking forward to your first pull request.

# Contact Support
Feel free to email mvpsolution@gmail.com of any questions.