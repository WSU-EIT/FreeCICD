namespace FreeCICD;

public partial class DataObjects
{
    public partial class UserGroup : ActionResponseObject
    {
        public DateTime Added { get; set; }
        public string? AddedBy { get; set; }
        public bool Deleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool Enabled { get; set; }
        public Guid GroupId { get; set; }
        public DateTime LastModified { get; set; }
        public string? LastModifiedBy { get; set; }
        public string? Name { get; set; }
        public UserGroupSettings Settings { get; set; } = new UserGroupSettings();
        public Guid TenantId { get; set; }
        public List<UserListing>? Users { get; set; }
    }

    public partial class UserGroupSettings
    {
        public string? SomeSetting { get; set; }
    }
}