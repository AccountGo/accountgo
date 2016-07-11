# AccountGo

Do you like the idea of sharing and giving back to the open source community?

Accounting System built in ASP.NET MVC is in early stage and lots of work to do but happy to share it to anyone. This will be very useful if you have future project to develop accounting system. We do the hard work for you!
It is initially designed for a small size businesses and the idea is to help them running efficient business by using Accounting System fit to them.

# Features

On a high level, this solution will provide modules including

1. Accounts Receivable
2. Accounts Payable
3. Inventory Control
4. Financial/Accounting

# Help Wanted

Wether you are a Developer, Consultant, Accountant, QA, Marketing expert, Project Manager we can all be part of this great and promising project.

If you are a developer and wanted to take part as contributor/collaborator we are happy to welcome you! To start with, you can visit the issues page and pick an issue that you would like to work on.

So go ahead, add your code. Looking forward to your first pull request.

# Contact Support
Feel free to email mvpsolution@gmail.com of any questions.

Don't miss regular source code updates, join http://accountgo.googlegroups.com

Also, you can view demo site here (http://www.accountgo.ph)

#THE FUTURE OF THE FRONT-END
The screenshot below will be the future front-end. It is heavily under-development and you could be part of it. The project is "AccountGoWeb" and consuming the "Api" project.

Technology Stack:
- ASP.NET Core
- ReactJS
- MobX, React-MobX
- Axios
- Bootstrap
- D3
- React-router (on some pages)

![accountgoweb](https://cloud.githubusercontent.com/assets/17961526/16177121/f41e2c10-3656-11e6-885f-fb2325b09066.PNG)

# Setup Develoment Environment
In “Presentation” folder, there are three web projects. 
-	“Web” – an old web front-end. Currently the demo site is http://www.accountgo.ph
-	“AccountGoWeb” – this is the future. Functionalities from the old "Web" front-end are soon transported to this project. Development under ASP.NET Core 1.0, ReactJS and MobX.
-	“Api” – ASP.NET REST API project to be consumed by “AccountGoWeb”.

AccountGoWeb/Api are development under ASP.NET Core 1.0, and requires you to install VS 2015 (Community is Ok) with Update 3 and the latest update of “Microsoft ASP.NET and Web Tools” extension.

“AccountGoWeb” use webpack so you also need to install webpack to the project. For some reason, Visual Studio 2015 "dependencies" is showing warning "Dependencies - not installed" when adding webpack to project.json. So for the time being, you need to install webpack manually. See instructions below.

1.	Install Visual 2015 (i.e. Community edition) with Update 3.
2.	Update “Microsoft ASP.NET and Web Tools” external tools. After you install VS 2015, go to "Tools->Extensions and updates" and search for “Microsoft ASP.NET and Web Tools”.
3.	Install “Webpack”. open command prompt and go to “AccountGoWeb” folder. Type “npm install webpack”.
4.	Install "Webpack Task Runner" Visual Studio 2015 extension. Go to "Tools->Extensions and updates" and search for “Webpack Task Runner”.
4.	Clone/Fork the latest repo here https://github.com/AccountGo/accountgo
5.	Open the solution file and restore all packages.

Note: You can use the database connection from the demo site. Look at the web.config ("Web") or appsettings.json ("Api") projects.

# Run the "Api" project
1. Right click on the project properties and go to Debug. Select "AccountGoApi" from the profile. This by default will run the api on "http://localhost:5000"
2. Make sure to update Config.apiUrl in "AccountGoWeb/webpack.config.js".
3. Set "accountgo" solution to "Multiple Startup Projects". Select "AccountGoWeb" and "Api".
