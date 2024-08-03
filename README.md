## REST API

## Project Description:

**This is a Rest API for the administration of employee records consisting of:**
- Simple Login (JWT authentication) (no registration page, administrator will create new users)
- Two User Roles: Employee, Administrator
- User Profile
- Projects and Tasks
- Employee can update his profile data and profile picture, create tasks that belong to the projects he is part of (also can assign these tasks to other employees that are part of the project), mark his tasks as completed etc.
Employee can view all tasks that are related to the projects he is part of (in read-only mode). Employee can not modify tasks which are not assigned to him.
- Administrator can create/update/remove users, projects and tasks. Administrator can add employees to projects (also remove employees from them), create new tasks and assign them to other employees, mark tasks as completed or remove them. Administrator cannot remove projects that have even open tasks.

## Technologies used:
- .NET SDK 6.0 (Lates 6.0.32)
- Entity Framework
- Microsoft SQL Server 2019
- Microsoft SQL Server Management Studio 19 (Database Management)
- Microsoft Visual Studio 2022

Models of the project are separated into a diferent project of type Class Library and then is added as reference to the WEB API solution. Model’s properties are enriched with Annotations for validation and documentation purposes.

Web Api project structure consist of Controllers, Services and Models with their repective DTOs. Each of the Service classes have their own Interface which is used with Dependency Injection in the Controller classes.
