namespace FreeCICD;

public partial class DataObjects
{

    public static partial class Endpoints
    {
        public static class SignalR
        {
            public const string Update = "api/Data/SignalRUpdate";
        }
    }

    public enum DeletePreference
    {
        Immediate,
        MarkAsDeleted,
    }

    public enum SettingType
    {
        Boolean,
        DateTime,
        EncryptedObject,
        EncryptedText,
        Guid,
        NumberDecimal,
        NumberDouble,
        NumberInt,
        Object,
        Text
    }

    public enum UserLookupType
    {
        Email,
        EmployeeId,
        Guid,
        Username
    }

    public class ActionResponseObject
    {
        public BooleanResponse ActionResponse { get; set; } = new BooleanResponse();
    }

    public class ActiveUser
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public bool Admin { get; set; }
        public string? TenantName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public DateTime? LastAccess { get; set; }
        public Guid? Photo { get; set; }
        public UserPreferences UserPreferences { get; set; } = new UserPreferences();
    }

    public class ApplicationSettings : ActionResponseObject
    {
        public string? ApplicationURL { get; set; }
        public string? DefaultReplyToAddress { get; set; }
        public string? DefaultTenantCode { get; set; }
        public string? EncryptionKey { get; set; }
        public DataObjects.MailServerConfig MailServerConfig { get; set; } = new MailServerConfig();
        public bool MaintenanceMode { get; set; }
        public bool UseTenantCodeInUrl { get; set; }
        public bool ShowTenantListingWhenMissingTenantCode { get; set; }
    }

    public class ApplicationSettingsUpdate
    {
        public string? ApplicationURL { get; set; }
        public string? DefaultTenantCode { get; set; }
        public bool MaintenanceMode { get; set; }
        public bool UseTenantCodeInUrl { get; set; }
        public bool ShowTenantListingWhenMissingTenantCode { get; set; }
    }

    public class Authenticate
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? TenantCode { get; set; }
        public Guid? TenantId { get; set; }
    }

    public class AuthenticationProviders
    {
        public bool UseApple { get; set; }
        public bool UseFacebook { get; set;  }
        public bool UseMicrosoftAccount { get; set; }
        public bool UseOpenId { get; set; }
        public string? OpenIdButtonText { get; set; }
        public string? OpenIdButtonClass { get; set; }
        public string? OpenIdButtonIcon { get; set; }
        public bool UseGoogle { get; set; }
    }

    public class BlazorDataModelLoader
    {
        public List<DataObjects.ActiveUser> ActiveUsers { get; set; } = new List<ActiveUser>();
        public CustomLoginProvider AdminCustomLoginProvider { get; set; } = new CustomLoginProvider();
        public List<Tenant> AllTenants { get; set; } = new List<Tenant>();
        public string ApplicationUrl { get; set; } = "";
        public DataObjects.ApplicationSettingsUpdate AppSettings { get; set; } = new ApplicationSettingsUpdate();
        public AuthenticationProviders? AuthenticationProviders { get; set; }
        public string CultureCode { get; set; } = "en-US";
        public List<OptionPair> CultureCodes { get; set; } = new List<OptionPair>();
        public Language DefaultLanguage { get; set; } = new Language();
        public List<Language> Languages { get; set; } = new List<Language>();
        public bool LoggedIn { get; set; }
        public DateOnly Released { get; set; } = DateOnly.FromDateTime(DateTime.Now);
        public Guid TenantId { get; set; } = Guid.Empty;
        public List<Tenant> Tenants { get; set; } = new List<Tenant>();
        public bool UseCustomAuthenticationProviderFromAdminAccount { get; set; }
        public User User { get; set; } = new User();
        public List<User> Users { get; set; } = new List<User>();
        public bool UseTenantCodeInUrl { get; set; }
        public string Version { get; set; } = "";
    }

    public class BooleanResponse
    {
        public List<string> Messages { get; set; } = new List<string>();
        public bool Result { get; set; }
    }

    public class ConnectionStringConfig : ActionResponseObject
    {
        public string? ConnectionString { get; set; }

        public string? SqlServer_Server { get; set; }
        public string? SqlServer_Database { get; set; }
        public string? SqlServer_UserId { get; set; }
        public string? SqlServer_Password { get; set; }
        public bool SqlServer_IntegratedSecurity { get; set; }
        public bool SqlServer_PersistSecurityInfo { get; set; }
        public bool SqlServer_TrustServerCertificate { get; set; }

        public ConnectionStringConfig()
        {
            this.ActionResponse = new BooleanResponse();
        }
    }

    public class CustomLoginProvider
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string ButtonClass { get; set; } = "";
        public string Code { get; set; } = "";
    }

    public class DataMigration
    {
        public List<string> Migration { get; set; } = new List<string>();
        public string MigrationId { get; set; } = String.Empty;
    }

    public class DeletedRecordCounts
    {
        public int FileStorage { get; set; }
        public int Users { get; set; }
    }

    public class DeletedRecordItem
    {
        public Guid ItemId { get; set; }
        public string Display { get; set; } = "";
        public DateTime DeletedAt { get; set; }
        public string DeletedBy { get; set; } = "";
    }

    public class DeletedRecords
    {
        public List<DeletedRecordItem> FileStorage { get; set; } = new List<DeletedRecordItem>();
        public List<DeletedRecordItem> Users { get; set; } = new List<DeletedRecordItem>();
    }

    public class Dictionary
    {
        public string? Key { get; set; }
        public string? Value { get; set; }
    }

    public class EmailMessage : ActionResponseObject
    {
        public string From { get; set; } = "";
        public string? FromDisplayName { get; set; }
        public string? ReplyTo { get; set; }
        public List<string> To { get; set; } = new List<string>();
        public List<string> Cc { get; set; } = new List<string>();
        public List<string> Bcc { get; set; } = new List<string>();
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public List<DataObjects.FileStorage>? Files { get; set; }
    }

    public class ExternalDataSource
    {
        public string Name { get; set; } = String.Empty;
        public string Type { get; set; } = String.Empty;
        public string? ConnectionString { get; set; } = String.Empty;
        public string Source { get; set; } = String.Empty;
        public int SortOrder { get; set; }
        public bool Active { get; set; }
    }

    public class FileStorage : ActionResponseObject
    {
        public Guid FileId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? ItemId { get; set; }
        public string? FileName { get; set; }
        public string? Extension { get; set; }
        public string? SourceFileId { get; set; }
        public long? Bytes { get; set; }
        public Byte[]? Value { get; set; }
        public DateTime UploadDate { get; set; }
        public string? UploadedBy { get; set; }
        public Guid? UserId { get; set; }
        public DateTime LastModified { get; set; }
        public string? LastModifiedBy { get; set; }
        public bool Deleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    public class Filter : ActionResponseObject
    {
        public Guid TenantId { get; set; }
        public Guid? FilterId { get; set; }
        public string? FilterName { get; set; }
        public double ExecutionTime { get; set; }
        public bool Loading { get; set; }
        public bool ShowFilters { get; set; }
        public bool IncludeDeletedItems { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string? Keyword { get; set; }
        public string? Sort { get; set; }
        public string? SortOrder { get; set; }
        public int RecordsPerPage { get; set; }
        public int PageCount { get; set; }
        public int RecordCount { get; set; }
        public int Page { get; set; }
        public string? Export { get; set; }
        public Guid[]? Tenants { get; set; } = new Guid[] { };
        public List<FilterColumn>? Columns { get; set; }
        public string? CultureCode { get; set; }
    }

    public class FilterColumn
    {
        public string? Align { get; set; }
        public string? Label { get; set; }
        public string? TipText { get; set; }
        public string? DataElementName { get; set; }
        public string? DataType { get; set; }
        public bool Sortable { get; set; }
        public string? Class { get; set; }
        public string? BooleanIcon { get; set; }
    }

    public class FilterFileStorage : Filter
    {
        public List<FileStorage>? Records { get; set; }
        public List<string> AvailableExtensions { get; set; } = new List<string>();
        public List<string> AvailableSources { get; set; } = new List<string>();
        public string[]? Extensions { get; set; } = new string[] { };
        public string[]? Source { get; set; } = new string[] { };
    }

    public class FilterUsers : Filter
    {
        public List<User>? Records { get; set; }
        public string? Enabled { get; set; }
        public string? Admin { get; set; }
    }

    public class Language
    {
        public Guid TenantId { get; set; }
        public string Culture { get; set; } = String.Empty;
        public string Description { get; set; } = String.Empty;
        public List<DataObjects.OptionPair> Phrases { get; set; } = new List<OptionPair>();
    }

    public class ListItem : ActionResponseObject
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string? Type { get; set; }
        public string? Name { get; set; }
        public int SortOrder { get; set; }
        public bool Enabled { get; set; }
    }

    public class LoginTenantListing
    {
        public List<Tenant> Tenants { get; set; } = new List<Tenant>();
        public List<Language> Languages { get; set; } = new List<Language>();
    }

    public class MailServerConfig
    {
        public string Type { get; set; } = "";
        public string? Config { get; set; }
        public bool AllowSendingFromIndividualEmailAddresses { get; set; }
    }

    public class MailServerConfigMicrosoftGraph
    {
        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? EmailAddress { get; set; }
    }

    public class MailServerConfigSMTP
    {
        public string? Server { get; set; }
        public int Port { get; set; }
        public bool UseSSL { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public class OptionPair
    {
        public string? Id { get; set; }
        public string? Value { get; set; }
    }

    public class PluginCache {
        public Guid RecordId { get; set; }
    }

    public class Setting : ActionResponseObject
    {
        public int SettingId { get; set; }
        public string SettingName { get; set; } = null!;
        public string? SettingType { get; set; }
        public string? SettingNotes { get; set; }
        public string? SettingText { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public DateTime LastModified { get; set; }
        public string? LastModifiedBy { get; set; }
    }

    public class SimplePost
    {
        public string? SingleItem { get; set; }
        public List<string> Items { get; set; } = new List<string>();
    }

    public class SimpleResponse
    {
        public bool Result { get; set; }
        public string? Message { get; set; }
    }

    public class Tenant : ActionResponseObject
    {
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string TenantCode { get; set; } = null!;
        public bool Enabled { get; set; }
        public DateTime Added { get; set; }
        public string? AddedBy { get; set; }
        public DateTime LastModified { get; set; }
        public string? LastModifiedBy { get; set; }
        public TenantSettings TenantSettings { get; set; } = new TenantSettings();
        public List<UserListing> Users { get; set; } = new List<UserListing>();
    }

    public class TenantList
    {
        public Guid TenantId { get; set; }
        public string Name { get; set; } = "";
        public string TenantCode { get; set; } = "";
    }

    public class TenantSettings
    {
        public List<string> AllowedFileTypes { get; set; } = new List<string>();
        public bool AllowUsersToManageAvatars { get; set; }
        public bool AllowUsersToManageBasicProfileInfo { get; set; }
        public List<string>? AllowUsersToManageBasicProfileInfoElements { get; set; }
        public bool AllowUsersToResetPasswordsForLocalLogin { get; set; }
        public bool AllowUsersToSignUpForLocalLogin { get; set; }
        public string? AppIcon { get; set; }
        public string? CustomAuthenticationButtonClass { get; set; }
        public string? CustomAuthenticationCode { get; set; }
        public string? CustomAuthenticationIcon { get; set; }
        public string? CustomAuthenticationName { get; set; }
        public string? DefaultCultureCode { get; set; }
        public string? DefaultReplyToAddress { get; set; }
        public DeletePreference DeletePreference { get; set; } = DeletePreference.MarkAsDeleted;
        public int DeleteMarkedRecordsAfterDays { get; set; } = 90;
        public bool HideAbout { get; set; }
        public string? LdapLookupRoot { get; set; }
        public string? LdapLookupUsername { get; set; }
        public string? LdapLookupPassword { get; set; }
        public string? LdapLookupSearchBase { get; set; }
        public string? LdapLookupLocationAttribute { get; set; }
        public int LdapLookupPort { get; set; }
        public List<string> LoginOptions { get; set; } = new List<string>();
        public Guid? Logo { get; set; }
        public bool LogoIncludedOnHomePage { get; set; }
        public bool LogoIncludedOnNavbar { get; set; }
        public int MaxToastMessages { get; set; } = -1;
        public List<string> ModuleHideElements { get; set; } = new List<string>();
        public List<string> ModuleOptInElements { get; set; } = new List<string>();
        public string? Theme { get; set; } = "";
        public string? ThemeCss { get; set; } = "";
        public string? ThemeFont { get; set; } = "";
        public string? ThemeFontCssImport { get; set; } = "";
        public bool RequirePreExistingAccountToLogIn { get; set; }
        public List<ListItem>? ListItems { get; set; } = null!;
        //public List<ExternalDataSource>? ExternalUserDataSources { get; set; }
        public string? JwtRsaPrivateKey { get; set; }
        public string? JwtRsaPublicKey { get; set; }
    }

    public class Test
    {
        public DateTime? TestDate { get; set; }
        public string? Value { get; set; }
    }

    public class User : ActionResponseObject
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Username { get; set; }
        public string? EmployeeId { get; set; }
        public string? Title { get; set; }
        public bool Enabled { get; set; }
        public DateTime? LastLogin { get; set; }
        public string? LastLoginSource { get; set; }
        public bool Admin { get; set; }
        public bool AppAdmin { get; set; }
        public bool ManageFiles { get; set; }
        public string? Password { get; set; }
        public bool PreventPasswordChange { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTime? LastLockoutDate { get; set; }
        public string? Source { get; set; }
        public DateTime Added { get; set; }
        public string? AddedBy { get; set; }
        public DateTime LastModified { get; set; }
        public string? LastModifiedBy { get; set; }
        public bool Deleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? Photo { get; set; }
        public bool HasLocalPassword { get; set; }
        public string? AuthToken { get; set; }
        public List<UserAccount> UserAccounts { get; set; } = new List<UserAccount>();
        public UserPreferences UserPreferences { get; set; } = new UserPreferences();
        public string? Confirmation { get; set; }
    }

    public class UserAccount
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public bool Enabled { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
    }

    public class UserListing
    {
        public Guid UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public bool Enabled { get; set; }
        public bool Deleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool Admin { get; set; }
    }

    public class UserPasswordReset
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public bool AllAccounts { get; set; }
    }

    public class UserPreferences
    {
        public bool EnabledItemsOnly { get; set; }
        public FilterFileStorage filterFileStorage { get; set; } = new FilterFileStorage();
        public FilterUsers filterUsers { get; set; } = new FilterUsers();
        public bool IncludeDeletedItems { get; set; }
        public string? LastNavigationId { get; set; }
        public string? LastUrl { get; set; }
        public string? LastView { get; set; }
        public bool StickyMenus { get; set; }
    }

    public class UserTenant
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string TenantCode { get; set; } = null!;
        public string TenantName { get; set; } = null!;
    }

    public class VersionInfo
    {
        public DateOnly Released { get; set; }
        public double RunningSince { get; set; }
        public string? Version { get; set; }
    }
}