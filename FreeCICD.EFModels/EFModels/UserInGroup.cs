using System;
using System.Collections.Generic;

namespace FreeCICD.EFModels.EFModels;

public partial class UserInGroup
{
    public virtual UserGroup Group { get; set; } = null!;

    public Guid GroupId { get; set; }

    public Guid TenantId { get; set; }

    public virtual User User { get; set; } = null!;

    public Guid UserId { get; set; }
    public Guid UserInGroupId { get; set; }
}