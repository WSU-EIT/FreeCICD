namespace FreeCICD;

public partial class DataObjects
{
    public partial class SignalRUpdateType
    {
        public const string Department = "Department";
        public const string DepartmentGroup = "DepartmentGroup";
        public const string File = "File";
        public const string Language = "Language";
        public const string LastAccessTime = "LastAccessTime";
        public const string Setting = "Setting";
        // {{ModuleItemStart:Tags}}
        public const string Tag = "Tag";
        // {{ModuleItemEnd:Tags}}
        public const string Tenant = "Tenant";
        public const string UDF = "UDF";
        public const string Undelete = "Undelete";
        public const string Unknown = "Unknown";
        public const string User = "User";
        public const string UserAttendance = "UserAttendance";
        public const string UserGroup = "UserGroup";
        public const string UserPreferences = "UserPreferences";

        // FreeCICD-specific SignalR update types
        public const string RegisterSignalR = "RegisterSignalR";
        public const string LoadingDevOpsInfoStatusUpdate = "LoadingDevOpsInfoStatusUpdate";
    }

    //public enum SignalRUpdateType
    //{
    //    Department,
    //    DepartmentGroup,
    //    File,
    //    Language,
    //    LastAccessTime,
    //    Setting,
    //    // {{ModuleItemStart:Tags}}
    //    Tag,
    //    // {{ModuleItemEnd:Tags}}
    //    Tenant,
    //    UDF,
    //    Undelete,
    //    Unknown,
    //    User,
    //    UserAttendance,
    //    UserGroup,
    //    UserPreferences,
    //}

    public partial class SignalRUpdate
    {
        public Guid? TenantId { get; set; }
        public Guid? ItemId { get; set; }
        public Guid? UserId { get; set; }
        public string? UserDisplayName { get; set; }
        //public SignalRUpdateType UpdateType { get; set; }
        public string UpdateType { get; set; } = "Unknown";
        public string Message { get; set; } = "";
        public object? Object { get; set; }
        public string? ObjectAsString { get; set; }
    }
}
