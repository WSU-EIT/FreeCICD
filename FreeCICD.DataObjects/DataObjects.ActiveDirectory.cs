namespace FreeCICD;

public partial class DataObjects
{
    public class ActiveDirectorySearchResults
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Department { get; set; }
        public string? Location { get; set; }
    }

    public class ActiveDirectoryUserInfo
    {
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string? Department { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? EmployeeId { get; set; }
        public string? Title { get; set; }
        public string? Location { get; set; }
    }
}