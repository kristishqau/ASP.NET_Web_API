namespace EmployeeData.Services
{
    public enum DeletionResult
    {
        Succeeded,
        NotFound,
        HasUnfinishedTasks,
        SaveFailed
    }
}
