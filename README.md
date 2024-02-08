REST API

Project Description:
You have a Rest API for the administration of employee records consisting of:
- Simple Login (preferable JWT authentication) (no registration page is needed, administrator will create new
users)
- Two User Roles: Employee, Administrator
- User Profile
- Projects and Tasks
- Employee can update his profile data and profile picture, create tasks that belong to the projects he is part of
(also can assign these tasks to other employees that are part of the project), mark his tasks as completed etc.
Employee can view all tasks that are related to the projects he is part of (in read-only mode). Employee can not
modify tasks which are not assigned to him.
- Administrator can create/update/remove users, projects and tasks. Administrator can add employees to
projects (also remove employees from them), create new tasks and assign them to other employees, mark tasks
as completed or remove them. Administrator cannot remove projects that have even open tasks.
Technologies to be used:
- .NET SDK 5.0 (Lates 5.0.102)
- Entity Framework (LINQ for querying data) (EFCore &amp; EFCore Tools)
- Dapper (for query writing) optional
- Microsoft SQL Server 2017 or 2019
- Microsoft SQL Server Management Studio 19 (Database Management)
- Microsoft Visual Studio 2019
Models of the project can be separated into a diferent project of type Class Library and then be added as reference to
the WEB API solution. Model’s properties must be enriched with Annotations for validation and documentation
purposes.
Web Api project structure must consist of Controllers, Services and ViewModels. Each of the Service classes must have
their own Interface which will be used with Dependency Injection in the Controller classes.
