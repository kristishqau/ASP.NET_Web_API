using EmployeeData.Models;
using EmployeeData.Services;
using System.Collections.Generic;
using System.Linq;

namespace EmployeeData
{
    public class Seed
    {
        private readonly AppDbContext dbContext;
        public Seed(AppDbContext context)
        {
            dbContext = context;
        }
        public void SeedDataContext()
        {
            if (!dbContext.UserProjects.Any())
            {
                var userProject = new UserProject()
                {
                    User = new User()
                    {
                        UserName = "admin",
                        Password = "adminpassword",
                        Role = "Administrator",
                        ProfilePictureUrl = "pp",
                        UserTasks = new List<UserTask>()
                        {
                            new UserTask
                            {
                                Task = new Task
                                {
                                    Title = "thirdtask",
                                    Description="desc",
                                    IsFinished=false
                                }
                            }
                        },
                    },
                    Project = new Project()
                    {
                        Name = "firstproject",
                        Description = "desc",
                        Tasks = new List<Task>()
                        {
                            new Task
                            {
                                Title = "firsttask",
                                Description = "desc",
                                IsFinished=false
                            },
                            new Task
                            {
                                Title = "secondTask",
                                Description = "desc",
                                IsFinished=true
                            }
                        }
                    }
                };
                if (!dbContext.UserProjects.Any())
                {
                    dbContext.UserProjects.AddRange(userProject);
                    dbContext.SaveChanges();
                }
            }
        }
    }
}