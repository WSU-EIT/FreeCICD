using System;
using System.Collections.Generic;

namespace FreeCICD.EFModels.EFModels;

public partial class EmailTemplate
{
    public DateTime Added { get; set; }

    public string? AddedBy { get; set; }

    public bool Deleted { get; set; }

    public DateTime? DeletedAt { get; set; }
    public Guid EmailTemplateId { get; set; }

    public bool Enabled { get; set; }

    public DateTime LastModified { get; set; }

    public string? LastModifiedBy { get; set; }

    public string Name { get; set; } = null!;

    public string? Template { get; set; }

    public Guid TenantId { get; set; }
}